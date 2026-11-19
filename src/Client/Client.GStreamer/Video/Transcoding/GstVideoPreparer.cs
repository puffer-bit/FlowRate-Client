using Client.Application.Chat;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.Core.Media;
using Client.GStreamer.Loader;
using Gst;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Client.GStreamer.Video.Transcoding;

/// <summary>
/// Compresses and remuxes attached videos with the media runtime, one at a time: two at once would only
/// share the encoder and the processor and finish no sooner.
/// </summary>
public sealed partial class GstVideoPreparer(ILogger<GstVideoPreparer> logger) : IVideoPreparer
{
    private static readonly string Folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flowrate-video");
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static int _cleaned;

    private static readonly string[] Elements = ["uridecodebin", "parsebin", "videoconvertscale", "mp4mux"];

    public bool IsAvailable => MediaRuntime.IsEnabled && GstLoader.Options != null &&
                               Elements.All(name => ElementFactory.Find(name) != null);

    public async Task<PreparedVideo?> PrepareAsync(ChatMediaSource source, VideoPreparation preparation,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return null;
        CleanOnce();
        Directory.CreateDirectory(Folder);

        // GStreamer reads a file by its path; what the platform gives only as a stream is copied first
        string? copy = null;
        var input = source.LocalPath;
        try
        {
            if (input == null || !File.Exists(input))
            {
                copy = input = NewPath(System.IO.Path.GetExtension(source.FileName));
                await using var from = await source.Open();
                await using var to = File.Create(copy);
                await from.CopyToAsync(to, cancellationToken);
            }

            var info = await Task.Run(() => VideoFileProbe.Probe(input, cancellationToken), cancellationToken);
            if (info == null)
                return null;

            var kind = VideoPreparationRules.Decide(info.Facts, preparation.Compress, preparation.StripMetadata, preparation.Preset);
            if (kind == VideoPreparationKind.AsIs)
                return new PreparedVideo(info.Facts, kind, null, source.FileName, source.Size);

            await OneAtATime.WaitAsync(cancellationToken);
            var output = NewPath(".mp4");
            try
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                var result = kind == VideoPreparationKind.Transcode
                    ? await VideoTranscoder.TranscodeAsync(input, output, info, preparation.Preset, progress, cancellationToken)
                    : await VideoTranscoder.RemuxAsync(input, output, info, progress, cancellationToken);
                var size = new FileInfo(output).Length;
                LogPrepared(logger, kind, result.VideoEncoder ?? "copy", result.Width, result.Height, result.FrameRate,
                    source.Size, size, started.Elapsed.TotalSeconds);
                return new PreparedVideo(info.Facts, kind, output, System.IO.Path.ChangeExtension(source.FileName, ".mp4"), size);
            }
            catch (VideoTranscodeException exception)
            {
                LogFailed(logger, exception, kind);
                throw new InvalidOperationException(exception.Message, exception);
            }
            finally
            {
                OneAtATime.Release();
            }
        }
        finally
        {
            if (copy != null)
                TryDelete(copy);
        }
    }

    private static string NewPath(string extension) =>
        System.IO.Path.Combine(Folder, Guid.NewGuid().ToString("N") + extension);

    // Files of a run that ended without cleaning up, a crash or a kill
    private static void CleanOnce()
    {
        if (Interlocked.Exchange(ref _cleaned, 1) != 0 || !Directory.Exists(Folder))
            return;
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            if (File.GetLastWriteTimeUtc(file) < System.DateTime.UtcNow.AddDays(-1))
                TryDelete(file);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Video prepared ({Kind}, {Encoder}): {Width}x{Height} at {FrameRate} fps, {Before} -> {After} bytes in {Seconds:F1} s.")]
    private static partial void LogPrepared(ILogger logger, VideoPreparationKind kind, string encoder, int width, int height,
        int frameRate, long before, long after, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Video could not be prepared ({Kind}).")]
    private static partial void LogFailed(ILogger logger, Exception exception, VideoPreparationKind kind);
}
