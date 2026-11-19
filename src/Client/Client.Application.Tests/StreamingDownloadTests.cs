using System.Collections;
using System.Collections.Concurrent;
using Client.Application.Chat;
using Client.Application.Ports;
using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public sealed class StreamingDownloadTests
{
    private const int Chunk = 1024;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static byte[] Content(int length) =>
        Enumerable.Range(0, length).Select(index => (byte)(index * 13 + index / 256)).ToArray();

    /// <summary>A server holding <paramref name="content"/> that notes every range asked for.</summary>
    private sealed class Server(byte[] content)
    {
        public ConcurrentQueue<(long Offset, long Length)> Requests { get; } = new();
        public Func<long, MediaTransferResult?> Fail { get; set; } = _ => null;
        public TimeSpan Delay { get; set; }

        public async Task<MediaTransferResult> FetchAsync(long offset, long length, Stream destination,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue((offset, length));
            if (Fail(offset) is { } failure)
                return failure;
            var end = (int)Math.Min(content.Length, offset + length);
            for (var position = (int)offset; position < end; position += Chunk / 2)
            {
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await destination.WriteAsync(content.AsMemory(position, Math.Min(Chunk / 2, end - position)),
                    cancellationToken);
            }
            return MediaTransferResult.NoError;
        }
    }

    private sealed class MemoryPartialFile(long length) : IPartialMediaFile
    {
        private readonly byte[] _data = new byte[length];
        private readonly BitArray _chunks = new((int)((length + Chunk - 1) / Chunk));

        public long Length => length;
        public int ChunkSize => Chunk;
        public int ChunkCount => _chunks.Length;
        public int ChunksPresent { get { lock (_data) return _chunks.Cast<bool>().Count(kept => kept); } }
        public string? CompletePath => ChunksPresent == ChunkCount ? "complete" : null;
        public bool Disposed { get; private set; }

        public bool HasChunk(int chunk) { lock (_data) return _chunks[chunk]; }

        public int Read(long position, Span<byte> buffer)
        {
            var chunk = (int)(position / Chunk);
            lock (_data)
            {
                if (position >= length || !_chunks[chunk])
                    return 0;
                var count = (int)Math.Min(buffer.Length, Math.Min(length, (long)(chunk + 1) * Chunk) - position);
                _data.AsSpan((int)position, count).CopyTo(buffer);
                return count;
            }
        }

        public void WriteChunk(int chunk, ReadOnlySpan<byte> data)
        {
            lock (_data)
            {
                data.CopyTo(_data.AsSpan(chunk * Chunk));
                _chunks[chunk] = true;
            }
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Read_WaitsForItsChunkAndGetsTheBytesOfTheFile()
    {
        var content = Content(Chunk * 5 + 300);
        var server = new Server(content);
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length), server.FetchAsync);

        var buffer = new byte[content.Length];
        Assert.Equal(content.Length, download.ReadFully(0, buffer));
        Assert.Equal(content, buffer);
        Assert.Equal(0, download.Read(content.Length, buffer));
    }

    [Fact]
    public async Task Read_FarAhead_IsFetchedBeforeTheRest()
    {
        var content = Content(Chunk * 64);
        var server = new Server(content) { Delay = TimeSpan.FromMilliseconds(5) };
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length), server.FetchAsync,
            readAhead: Chunk * 64);

        var buffer = new byte[100];
        await Task.Run(() => download.ReadFully(Chunk * 60 + 10, buffer)).WaitAsync(Patience);
        Assert.Equal(content.AsSpan(Chunk * 60 + 10, 100).ToArray(), buffer);
        Assert.Contains(server.Requests, request => request.Offset == Chunk * 60);
        Assert.False(download.IsComplete);
    }

    [Fact]
    public async Task NothingIsFetched_BeyondTheReadAhead()
    {
        var content = Content(Chunk * 20);
        var server = new Server(content);
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length), server.FetchAsync,
            readAhead: Chunk * 4);

        download.ReadFully(0, new byte[10]);
        await Task.Delay(200);
        // Ten bytes read: the read-ahead reaches into the fifth chunk
        Assert.All(server.Requests, request => Assert.True(request.Offset + request.Length <= Chunk * 5));
        Assert.InRange(download.Progress, 0.15, 0.25);
    }

    [Fact]
    public async Task WholeFile_IsFetchedAndCompletes()
    {
        var content = Content(Chunk * 30 + 1);
        var server = new Server(content);
        var file = new MemoryPartialFile(content.Length);
        var progress = new List<double>();
        using var download = new StreamingDownload(file, server.FetchAsync, new SyncProgress(progress.Add),
            wholeFile: true);

        Assert.Equal(MediaTransferResult.NoError, await download.WaitCompleteAsync(CancellationToken.None).WaitAsync(Patience));
        Assert.True(download.IsComplete);
        Assert.Equal(1, progress[^1]);
        Assert.Equal([(0d, 1d)], download.GetKeptRanges());
    }

    [Fact]
    public async Task MissingFile_FailsTheWaitingRead()
    {
        var content = Content(Chunk * 4);
        var server = new Server(content) { Fail = _ => MediaTransferResult.NotFound };
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length), server.FetchAsync);

        await Assert.ThrowsAsync<IOException>(() => Task.Run(() => download.Read(0, new byte[10])).WaitAsync(Patience));
        Assert.Equal(MediaTransferResult.NotFound, download.Failure);
    }

    [Fact]
    public async Task BrokenConnection_IsTriedAgain()
    {
        var content = Content(Chunk * 4);
        var failures = 2;
        var server = new Server(content)
        {
            Fail = _ => Interlocked.Decrement(ref failures) >= 0 ? MediaTransferResult.ConnectionFailed : null
        };
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length), server.FetchAsync);

        var buffer = new byte[10];
        await Task.Run(() => download.Read(0, buffer)).WaitAsync(Patience);
        Assert.Equal(content.AsSpan(0, 10).ToArray(), buffer);
    }

    [Fact]
    public async Task CanceledRead_StopsWaiting()
    {
        var content = Content(Chunk * 4);
        var server = new Server(content) { Delay = TimeSpan.FromSeconds(30) };
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length), server.FetchAsync);
        using var cancel = new CancellationTokenSource();

        var read = Task.Run(() => download.Read(0, new byte[10], cancel.Token));
        await Task.Delay(100);
        Assert.True(download.IsWaiting);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(Patience));
        Assert.False(download.IsWaiting);
    }

    [Fact]
    public async Task Dispose_EndsAWaitingReadAndLetsGoOfTheFile()
    {
        var content = Content(Chunk * 4);
        var server = new Server(content) { Delay = TimeSpan.FromSeconds(30) };
        var file = new MemoryPartialFile(content.Length);
        var download = new StreamingDownload(file, server.FetchAsync);

        var read = Task.Run(() => download.Read(0, new byte[10]));
        await Task.Delay(100);
        download.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => read.WaitAsync(Patience));
        Assert.True(file.Disposed);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
