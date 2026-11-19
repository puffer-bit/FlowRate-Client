using Client.Application.Ports;
using Client.Core.Chat;
using Microsoft.Extensions.Logging;

namespace Client.Application.Chat;

/// <summary>
/// Downloads an attachment chunk by chunk into its <see cref="IPartialMediaFile"/>, in the order a player reads it,
/// so it can be played while it arrives. A read waits for the chunk it needs, and that chunk is fetched before
/// anything else. Ahead of the last read the chunks up to <see cref="ReadAhead"/> bytes are fetched in the
/// background; the rest only when <c>wholeFile</c> is asked for. Reads may come from any thread and block.
/// </summary>
public sealed class StreamingDownload : IDisposable
{
    /// <summary>Fetches <c>length</c> bytes from <c>offset</c> into the stream, or fewer when the file ends.</summary>
    public delegate Task<MediaTransferResult> RangeFetcher(long offset, long length, Stream destination,
        CancellationToken cancellationToken);

    /// <summary>How far ahead of the last read chunks are fetched: about a minute of a compressed 720p video.</summary>
    public const long DefaultReadAhead = 16L * 1024 * 1024;

    // One request asks for this many chunks at most, so a read elsewhere does not wait long for its turn; a whole
    // file nobody reads from goes in longer requests
    private const int MaxChunksPerRequest = 8;
    private const int MaxChunksPerWholeFileRequest = 64;

    // Pauses before a failed request is tried again; the download gives up after the last one
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(0.5),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    private readonly IPartialMediaFile _file;
    private readonly RangeFetcher _fetch;
    private readonly IProgress<double>? _progress;
    private readonly ILogger? _logger;
    private readonly bool _wholeFile;
    // A monitor, not a Lock: reads wait on it for their chunk
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stopped = new();
    private readonly TaskCompletionSource<MediaTransferResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<int> _waiting = [];
    private long _playhead;
    private (int First, int Count, CancellationTokenSource Cancel)? _request;
    private MediaTransferResult? _failure;
    private bool _disposed;

    /// <param name="progress">Told the share of the file kept, from 0 to 1, after every chunk.</param>
    /// <param name="wholeFile">Fetches every chunk, not only those ahead of the reads.</param>
    public StreamingDownload(IPartialMediaFile file, RangeFetcher fetch, IProgress<double>? progress = null,
        ILogger? logger = null, bool wholeFile = false, long readAhead = DefaultReadAhead)
    {
        _file = file;
        _fetch = fetch;
        _progress = progress;
        _logger = logger;
        _wholeFile = wholeFile;
        ReadAhead = readAhead;
        if (IsComplete)
            _completion.TrySetResult(MediaTransferResult.NoError);
        _ = Task.Run(RunAsync);
    }

    public long Length => _file.Length;

    public long ReadAhead { get; }

    public bool IsComplete => _file.ChunksPresent == _file.ChunkCount;

    /// <summary>The share of the file kept, from 0 to 1.</summary>
    public double Progress => (double)_file.ChunksPresent / _file.ChunkCount;

    /// <summary>The whole file in the cache, once every chunk is in.</summary>
    public string? CompletePath => _file.CompletePath;

    /// <summary>True while a read waits for its chunk to arrive.</summary>
    public bool IsWaiting
    {
        get
        {
            lock (_gate)
                return _waiting.Count > 0;
        }
    }

    /// <summary>Why the download stopped for good, or null while it goes on or once it finished.</summary>
    public MediaTransferResult? Failure
    {
        get
        {
            lock (_gate)
                return _failure;
        }
    }

    /// <summary>
    /// The parts of the file kept, as shares of its length from 0 to 1, in order.
    /// </summary>
    public IReadOnlyList<(double Start, double End)> GetKeptRanges()
    {
        var ranges = new List<(double, double)>();
        var count = _file.ChunkCount;
        for (var chunk = 0; chunk < count; chunk++)
        {
            if (!_file.HasChunk(chunk))
                continue;
            var first = chunk;
            while (chunk + 1 < count && _file.HasChunk(chunk + 1))
                chunk++;
            ranges.Add(((double)first / count, (double)(chunk + 1) / count));
        }
        return ranges;
    }

    /// <summary>Ends when every chunk is kept, or with why the download stopped.</summary>
    public Task<MediaTransferResult> WaitCompleteAsync(CancellationToken cancellationToken) =>
        _completion.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Reads at least one byte from <paramref name="position"/> on, waiting for the chunk it is in.
    /// </summary>
    /// <returns>How many bytes were read; 0 only past the end of the file.</returns>
    /// <exception cref="IOException">The download stopped for good.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public int Read(long position, Span<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (position < 0 || position >= Length || buffer.IsEmpty)
            return 0;

        var chunk = (int)(position / _file.ChunkSize);
        while (true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var read = _file.Read(position, buffer);
            if (read > 0)
            {
                MoveForward(position + read);
                return read;
            }

            lock (_gate)
            {
                if (_file.HasChunk(chunk))
                    continue;
                if (_failure is { } failure)
                    throw new IOException($"The download stopped: {failure}.");
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();

                _playhead = position;
                _waiting.Add(chunk);
                // The request in progress is for something else: the read goes first
                if (_request is { } request && (chunk < request.First || chunk >= request.First + request.Count))
                    request.Cancel.Cancel();
                Wake();
                try
                {
                    using var canceled = cancellationToken.Register(PulseAll);
                    while (!_file.HasChunk(chunk) && _failure == null && !_disposed
                           && !cancellationToken.IsCancellationRequested)
                        Monitor.Wait(_gate);
                }
                finally
                {
                    _waiting.Remove(chunk);
                }
            }
        }
    }

    /// <summary>Reads until <paramref name="buffer"/> is full or the file ends.</summary>
    /// <inheritdoc cref="Read"/>
    public int ReadFully(long position, Span<byte> buffer, CancellationToken cancellationToken = default)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = Read(position + total, buffer[total..], cancellationToken);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private void MoveForward(long position)
    {
        lock (_gate)
        {
            // Reads past what is fetched ahead let the downloader go on
            _playhead = position;
            Wake();
        }
    }

    private void PulseAll()
    {
        lock (_gate)
            Monitor.PulseAll(_gate);
    }

    // Under the gate
    private void Wake()
    {
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    private async Task RunAsync()
    {
        var failures = 0;
        try
        {
            while (true)
            {
                (int First, int Count)? next;
                CancellationTokenSource? cancel = null;
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    next = PickNext();
                    if (next is { } picked)
                    {
                        cancel = CancellationTokenSource.CreateLinkedTokenSource(_stopped.Token);
                        _request = (picked.First, picked.Count, cancel);
                    }
                }

                if (next is not { } range)
                {
                    if (IsComplete)
                    {
                        Finish(null);
                        return;
                    }
                    await _wake.WaitAsync(_stopped.Token);
                    continue;
                }

                MediaTransferResult result;
                using (cancel)
                {
                    result = await FetchAsync(range.First, range.Count, cancel!.Token);
                    lock (_gate)
                        _request = null;
                }

                if (_stopped.IsCancellationRequested)
                    return;
                // Put aside for a read elsewhere, or done
                if (result is MediaTransferResult.Canceled or MediaTransferResult.NoError)
                {
                    if (result == MediaTransferResult.NoError)
                        failures = 0;
                    continue;
                }

                _logger?.LogDebug("Streaming download of chunk {Chunk} failed: {Result}", range.First, result);
                if (result is MediaTransferResult.NotFound or MediaTransferResult.NotAllowed
                        or MediaTransferResult.Unauthorized || failures >= RetryDelays.Length)
                {
                    Finish(result);
                    return;
                }
                await Task.Delay(RetryDelays[failures++], _stopped.Token);
            }
        }
        // Disposed in the middle of a request
        catch (Exception) when (_stopped.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Streaming download failed.");
            Finish(MediaTransferResult.Failed);
        }
        finally
        {
            _completion.TrySetResult(Failure ?? (IsComplete ? MediaTransferResult.NoError : MediaTransferResult.Canceled));
        }
    }

    // Under the gate: a waiting read first, then what lies ahead of the reads, then for a whole file the rest
    private (int First, int Count)? PickNext()
    {
        var count = _file.ChunkCount;
        var first = -1;
        for (var index = _waiting.Count - 1; index >= 0 && first < 0; index--)
            if (!_file.HasChunk(_waiting[index]))
                first = _waiting[index];

        var from = (int)Math.Min(count, _playhead / _file.ChunkSize);
        var until = _wholeFile ? count
            : (int)Math.Min(count, (Math.Min(Length, _playhead + ReadAhead) + _file.ChunkSize - 1) / _file.ChunkSize);
        if (first < 0)
        {
            first = FirstMissing(from, until);
            if (first < 0 && _wholeFile)
                first = FirstMissing(0, from);
        }

        if (first < 0)
            return null;

        // A request does not reach past what is wanted ahead, so a seek that follows does not wait for it
        var end = first >= from && first < until ? until : count;
        if (!_wholeFile && (first < from || first >= until))
            end = Math.Min(count, first + (int)Math.Max(1, ReadAhead / _file.ChunkSize));
        var most = _wholeFile && _waiting.Count == 0 ? MaxChunksPerWholeFileRequest : MaxChunksPerRequest;
        var last = first;
        while (last + 1 < end && last + 1 - first < most && !_file.HasChunk(last + 1))
            last++;
        return (first, last - first + 1);
    }

    private int FirstMissing(int from, int until)
    {
        for (var chunk = from; chunk < until; chunk++)
            if (!_file.HasChunk(chunk))
                return chunk;
        return -1;
    }

    private async Task<MediaTransferResult> FetchAsync(int first, int count, CancellationToken cancellationToken)
    {
        var offset = (long)first * _file.ChunkSize;
        var length = Math.Min(Length - offset, (long)count * _file.ChunkSize);
        var sink = new ChunkSink(this, first, length);
        MediaTransferResult result;
        try
        {
            result = await _fetch(offset, length, sink, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MediaTransferResult.Canceled;
        }
        if (result == MediaTransferResult.NoError && !sink.IsWhole)
            return MediaTransferResult.ConnectionFailed;
        return result;
    }

    private void ChunkArrived(int chunk, ReadOnlySpan<byte> data)
    {
        _file.WriteChunk(chunk, data);
        PulseAll();
        _progress?.Report(Progress);
    }

    private void Finish(MediaTransferResult? failure)
    {
        lock (_gate)
        {
            _failure ??= failure;
            Monitor.PulseAll(_gate);
        }
        _completion.TrySetResult(failure ?? MediaTransferResult.NoError);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            Monitor.PulseAll(_gate);
        }
        _stopped.Cancel();
        _file.Dispose();
    }

    /// <summary>Takes what a request brings and keeps every chunk as soon as it is whole.</summary>
    private sealed class ChunkSink(StreamingDownload owner, int first, long length) : Stream
    {
        private readonly byte[] _buffer = new byte[owner._file.ChunkSize];
        private int _chunk = first;
        private int _filled;
        private long _received;

        public bool IsWhole => _received == length;

        public override void Write(ReadOnlySpan<byte> data)
        {
            if (_received + data.Length > length)
                throw new IOException("The server sent more than was asked for.");

            while (!data.IsEmpty)
            {
                var chunkLength = (int)Math.Min(_buffer.Length, owner.Length - (long)_chunk * _buffer.Length);
                var take = Math.Min(chunkLength - _filled, data.Length);
                data[..take].CopyTo(_buffer.AsSpan(_filled));
                _filled += take;
                _received += take;
                data = data[take..];
                if (_filled == chunkLength)
                {
                    owner.ChunkArrived(_chunk, _buffer.AsSpan(0, chunkLength));
                    _chunk++;
                    _filled = 0;
                }
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _received; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
