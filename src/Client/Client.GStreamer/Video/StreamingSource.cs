using System.Buffers;
using Client.Application.Chat;
using Gst;
using Gst.App;

namespace Client.GStreamer.Video;

/// <summary>
/// Feeds a pipeline from an attachment as it downloads: its <c>appsrc</c> is set up for random access, so the
/// demuxer asks for the bytes it needs and a read waits until they arrive.
/// </summary>
internal sealed class StreamingSource
{
    // What one push carries at most; the demuxer asks for less nearly always
    private const int MaxPush = 4 * 1024 * 1024;
    private const int DefaultPush = 64 * 1024;

    private readonly StreamingDownload _download;
    private CancellationTokenSource _reads = new();
    private long _offset;
    private Exception? _failure;
    private volatile bool _stopped;

    // Kept alive while the source may call them
    private readonly AppSrcNeedDataCallback _needData;
    private readonly AppSrcEnoughDataCallback _enoughData;
    private readonly AppSrcSeekDataCallback _seekData;

    private readonly string? _caps;

    /// <param name="fileName">Its extension names the container, when it is one this knows.</param>
    public StreamingSource(StreamingDownload download, string? fileName)
    {
        _download = download;
        _caps = CapsOf(fileName);
        _needData = NeedData;
        _enoughData = _ => { };
        _seekData = SeekData;
    }

    public const string Uri = "appsrc://";

    /// <summary>Why the stream could not go on, once it cannot.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>True while the player waits for bytes that have not arrived yet.</summary>
    public bool IsWaiting => _download.IsWaiting;

    /// <summary>
    /// The container of a file by its extension. Known up front, it spares finding the type, which reads the end
    /// of the file too and so would download it first.
    /// </summary>
    internal static string? CapsOf(string? fileName) =>
        Path.GetExtension(fileName)?.ToLowerInvariant() switch
        {
            ".mp4" or ".mov" or ".m4a" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".wav" => "audio/x-wav",
            // MP3 is found by its frames, and its tags may be at the end anyway
            _ => null
        };

    /// <summary>
    /// Tells the type finder of <paramref name="pipeline"/> the container, when the name gives it. Caps set on the
    /// source itself would arrive in the middle of a pull and stall the type finder.
    /// </summary>
    public void ForceType(Bin pipeline)
    {
        if (_caps == null)
            return;
        pipeline.DeepElementAdded += (_, args) =>
        {
            if (args.Element.GetFactory()?.GetName() != "typefind")
                return;
            using var caps = Caps.FromString(_caps);
            args.Element.SetProperty("force-caps", caps);
        };
    }

    /// <summary>Takes the source of the pipeline: from the <c>source-setup</c> signal of <c>playbin</c>.</summary>
    public void Attach(Element element)
    {
        if (element is not AppSrc source)
            throw new InvalidOperationException($"The media source is {element.GetType().Name}, not appsrc.");
        source.SetStreamType(AppStreamType.RandomAccess);
        source.SetSize(_download.Length);
        source.SetSimpleCallbacks(_needData, _enoughData, _seekData);
    }

    /// <summary>
    /// Ends the read that waits, so the streaming thread is free for a seek or for stopping: the source then
    /// waits for the flush instead of the bytes.
    /// </summary>
    public void InterruptReads()
    {
        var reads = Interlocked.Exchange(ref _reads, new CancellationTokenSource());
        reads.Cancel();
    }

    /// <summary>Ends the read that waits and every one after, so the pipeline can be stopped.</summary>
    public void Stop()
    {
        _stopped = true;
        InterruptReads();
    }

    private void NeedData(AppSrc source, uint length)
    {
        if (_stopped)
            return;
        var offset = Interlocked.Read(ref _offset);
        var remaining = _download.Length - offset;
        if (remaining <= 0)
        {
            source.EndOfStream();
            return;
        }

        var wanted = (int)Math.Min(remaining, length is 0 or uint.MaxValue ? DefaultPush : Math.Min(length, MaxPush));
        var buffer = ArrayPool<byte>.Shared.Rent(wanted);
        try
        {
            // The demuxer takes a short buffer for the end of the file, so all of it is read
            var read = _download.ReadFully(offset, buffer.AsSpan(0, wanted), Volatile.Read(ref _reads).Token);
            if (read == 0)
            {
                source.EndOfStream();
                return;
            }

            using var pushed = Gst.Buffer.NewMemdup(buffer.AsSpan(0, read));
            pushed.SetOffset((ulong)offset);
            Interlocked.Exchange(ref _offset, offset + read);
            source.PushBuffer(pushed);
        }
        catch (OperationCanceledException)
        {
            // A seek or stop is on its way and flushes the source
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Interlocked.CompareExchange(ref _failure, exception, null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private bool SeekData(AppSrc source, ulong offset)
    {
        Interlocked.Exchange(ref _offset, (long)offset);
        return true;
    }
}
