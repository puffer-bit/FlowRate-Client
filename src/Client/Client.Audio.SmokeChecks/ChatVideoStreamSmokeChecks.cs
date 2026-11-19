using System.Collections;
using System.Diagnostics;
using Client.Application.Chat;
using Client.Application.Ports;
using System.Collections.Concurrent;
using Client.Core.Chat;
using Client.GStreamer.Audio;
using Client.GStreamer.Video;

/// <summary>
/// Plays generated MP4 files the way a chat video plays while it downloads: through <see cref="StreamingDownload"/>
/// from a slow fake server, with the index at the start and at the end, a seek ahead of what arrived, and to the end.
/// </summary>
internal static class ChatVideoStreamSmokeChecks
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "flowrate-chat-video-stream-smoke");
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        foreach (var indexAtEnd in new[] { false, true })
        {
            var path = Path.Combine(Folder, indexAtEnd ? "index-at-end.mp4" : "faststart.mp4");
            VideoTranscodeSmokeChecks.Generate(path, 640, 360, 30, 12,
                "openh264enc bitrate=1500000 ! h264parse",
                indexAtEnd ? "mp4mux name=mux" : "mp4mux name=mux faststart=true", "fdkaacenc",
                rotate: false, tagged: false, interleaveAtEnd: indexAtEnd);
            Check(path, indexAtEnd);
        }
        CheckStopWhileWaiting(Path.Combine(Folder, "faststart.mp4"));
        CheckAudio(WriteWav(Path.Combine(Folder, "tone.wav"), seconds: 12));
        if (Gst.ElementFactory.Make("lamemp3enc", null) is { } lame)
        {
            lame.Dispose();
            var mp3 = Path.Combine(Folder, "tone.mp3");
            using var pipeline = (Gst.Pipeline)Gst.Global.ParseLaunch(
                $"audiotestsrc num-buffers={12 * 48000 / 1024} samplesperbuffer=1024 ! audio/x-raw,rate=48000,channels=2 ! " +
                $"audioconvert ! lamemp3enc ! filesink location=\"{mp3}\"");
            pipeline.SetState(Gst.State.Playing);
            using var bus = pipeline.GetBus();
            using var done = bus.TimedPopFiltered(Gst.ClockTime.FromNanoseconds(60_000_000_000),
                Gst.MessageType.Eos | Gst.MessageType.Error);
            pipeline.SetState(Gst.State.Null);
            CheckAudio(mp3);
        }
        else
            Console.WriteLine("  mp3: no encoder in this runtime, skipped");
        CheckFailure(Path.Combine(Folder, "faststart.mp4"));
        Console.WriteLine("Chat video streaming smoke checks passed.");
    }

    private static void Check(string path, bool indexAtEnd)
    {
        var content = File.ReadAllBytes(path);
        var what = indexAtEnd ? "index at the end" : "index at the start";
        // About 4 Mbit/s, a little more than the video needs
        var server = new SlowServer(content, bytesPerSecond: 500_000);
        var file = new MemoryPartialFile(content.Length, 64 * 1024);
        using var download = new StreamingDownload(file, server.FetchAsync, readAhead: 1024 * 1024);

        var frames = 0;
        var clock = Stopwatch.StartNew();
        using (var playback = new ChatVideoPlayback(download, path, _ => Interlocked.Increment(ref frames), volume: 0))
        {
            WaitFor(playback, () => playback.Position > 1.5, $"{what}: plays before it has arrived");
            // The container is known from the name, so nothing reads the end of the file to find its type
            if (!indexAtEnd)
                Require(server.Offsets.All(offset => offset < content.Length - 64 * 1024),
                    $"{what}: the end is not downloaded first ({string.Join(", ", server.Offsets)})");
            Console.WriteLine($"  {what}: playing after {clock.Elapsed.TotalSeconds:0.0} s with " +
                              $"{download.Progress:P0} of {content.Length / 1024} KiB, {server.Requested / 1024} KiB requested");
            Require(download.Progress < 0.9, $"{what}: started before most of the file arrived ({download.Progress:P0})");

            var before = server.Requested;
            var seeking = Stopwatch.StartNew();
            playback.Seek(9);
            // The UI thread seeks: it must not wait for the network
            Require(seeking.Elapsed < TimeSpan.FromMilliseconds(500),
                $"{what}: the seek returns at once ({seeking.Elapsed.TotalMilliseconds:0} ms)");
            WaitFor(playback, () => playback.Position > 9.5, $"{what}: plays on after a seek ahead");
            Console.WriteLine($"  {what}: seek to 9 s played after {server.Requested - before} more bytes requested");

            var seen = Volatile.Read(ref frames);
            WaitFor(playback, () => playback.HasEnded, $"{what}: reaches the end");
            Require(Volatile.Read(ref frames) > seen, $"{what}: frames arrive after the seek");
            Console.WriteLine($"  {what}: ended after {clock.Elapsed.TotalSeconds:0.0} s, {frames} frames, " +
                              $"{download.Progress:P0} kept");
        }
        // Not everything between the start and the seek is fetched
        Require(!download.IsComplete || indexAtEnd, $"{what}: the skipped part was not downloaded");
    }

    private static void CheckStopWhileWaiting(string path)
    {
        var content = File.ReadAllBytes(path);
        var server = new SlowServer(content, bytesPerSecond: 20_000);
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length, 64 * 1024), server.FetchAsync);
        var playback = new ChatVideoPlayback(download, path, _ => { }, volume: 0);
        Thread.Sleep(1500);
        playback.Update();
        Require(download.IsWaiting, "stop while waiting: the player waits for bytes");
        var clock = Stopwatch.StartNew();
        playback.Dispose();
        Require(clock.Elapsed < TimeSpan.FromSeconds(2), $"stop while waiting: stops at once ({clock.Elapsed.TotalSeconds:0.0} s)");
        Console.WriteLine($"  stop while waiting: stopped in {clock.Elapsed.TotalMilliseconds:0} ms");
    }

    private static void CheckFailure(string path)
    {
        var content = File.ReadAllBytes(path);
        var server = new SlowServer(content, bytesPerSecond: 2_000_000) { FailFrom = content.Length / 2 };
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length, 64 * 1024), server.FetchAsync,
            readAhead: 256 * 1024);
        using var playback = new ChatVideoPlayback(download, path, _ => { }, volume: 0);
        var clock = Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed < Patience)
            {
                Thread.Sleep(50);
                playback.Update();
                Require(!playback.HasEnded, "download failure: does not look like the end of the video");
            }
            Require(false, "download failure: is reported");
        }
        catch (IOException exception)
        {
            Console.WriteLine($"  download failure: reported after {clock.Elapsed.TotalSeconds:0.0} s ({exception.InnerException?.Message})");
        }
    }

    private static void CheckAudio(string path)
    {
        var content = File.ReadAllBytes(path);
        var what = Path.GetExtension(path).TrimStart('.');
        // About twice as fast as the audio plays
        var server = new SlowServer(content, bytesPerSecond: Math.Max(64_000, content.Length / 6));
        using var download = new StreamingDownload(new MemoryPartialFile(content.Length, 64 * 1024), server.FetchAsync,
            readAhead: 256 * 1024);
        using var playback = new ChatAudioPlayback(download, path, "fakesink sync=true", volume: 0);
        var clock = Stopwatch.StartNew();
        while (playback.Position < 1)
        {
            Require(clock.Elapsed < Patience, $"{what}: plays before it has arrived (at {playback.Position:0.0} s)");
            Thread.Sleep(50);
            playback.Update();
        }
        Console.WriteLine($"  {what}: playing after {clock.Elapsed.TotalSeconds:0.0} s with {download.Progress:P0} " +
                          $"of {content.Length / 1024} KiB");
        Require(download.Progress < 0.9, $"{what}: started before most of the file arrived ({download.Progress:P0})");
        playback.Seek(9);
        while (!playback.HasEnded)
        {
            Require(clock.Elapsed < Patience, $"{what}: reaches the end after a seek (at {playback.Position:0.0} s)");
            Thread.Sleep(50);
            playback.Update();
        }
        Console.WriteLine($"  {what}: ended after {clock.Elapsed.TotalSeconds:0.0} s, {download.Progress:P0} kept");
        Require(!download.IsComplete, $"{what}: the skipped part was not downloaded");
    }

    private static string WriteWav(string path, int seconds)
    {
        const int Rate = 48000;
        var samples = Rate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var index = 0; index < samples; index++)
            writer.Write((short)(Math.Sin(index * 2 * Math.PI * 440 / Rate) * 8000));
        return path;
    }

    private static void WaitFor(ChatVideoPlayback playback, Func<bool> condition, string description)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Require(clock.Elapsed < Patience, $"{description} (timed out at {playback.Position:0.0} s)");
            Thread.Sleep(50);
            playback.Update();
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("Chat video streaming smoke check failed: " + description);
    }

    private sealed class SlowServer(byte[] content, int bytesPerSecond)
    {
        private long _requested;
        public ConcurrentQueue<long> Offsets { get; } = new();
        public long FailFrom { get; init; } = long.MaxValue;
        public long Requested => Interlocked.Read(ref _requested);

        public async Task<MediaTransferResult> FetchAsync(long offset, long length, Stream destination,
            CancellationToken cancellationToken)
        {
            const int Piece = 16 * 1024;
            Offsets.Enqueue(offset);
            await Task.Delay(30, cancellationToken);
            if (offset + length > FailFrom)
                return MediaTransferResult.NotFound;
            var end = (int)Math.Min(content.Length, offset + length);
            for (var position = (int)offset; position < end; position += Piece)
            {
                var count = Math.Min(Piece, end - position);
                await Task.Delay(TimeSpan.FromSeconds((double)count / bytesPerSecond), cancellationToken);
                await destination.WriteAsync(content.AsMemory(position, count), cancellationToken);
                Interlocked.Add(ref _requested, count);
            }
            return MediaTransferResult.NoError;
        }
    }

    private sealed class MemoryPartialFile(long length, int chunkSize) : IPartialMediaFile
    {
        private readonly byte[] _data = new byte[length];
        private readonly BitArray _chunks = new((int)((length + chunkSize - 1) / chunkSize));
        private int _present;

        public long Length => length;
        public int ChunkSize => chunkSize;
        public int ChunkCount => _chunks.Length;
        public int ChunksPresent => Volatile.Read(ref _present);
        public string? CompletePath => null;

        public bool HasChunk(int chunk) { lock (_data) return _chunks[chunk]; }

        public int Read(long position, Span<byte> buffer)
        {
            var chunk = (int)(position / chunkSize);
            lock (_data)
            {
                if (position >= length || !_chunks[chunk])
                    return 0;
                var count = (int)Math.Min(buffer.Length, Math.Min(length, (long)(chunk + 1) * chunkSize) - position);
                _data.AsSpan((int)position, count).CopyTo(buffer);
                return count;
            }
        }

        public void WriteChunk(int chunk, ReadOnlySpan<byte> data)
        {
            lock (_data)
            {
                if (_chunks[chunk])
                    return;
                data.CopyTo(_data.AsSpan(chunk * chunkSize));
                _chunks[chunk] = true;
                _present++;
            }
        }

        public void Dispose() { }
    }
}
