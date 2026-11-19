using Client.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Infrastructure.Tests;

public sealed class PartialMediaFileTests : IDisposable
{
    private const int Chunk = PartialMediaFile.DefaultChunkSize;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"partial-media-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private MediaCache Cache() => new(_directory, NullLogger<MediaCache>.Instance);

    private static byte[] Content(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
            bytes[index] = (byte)(index * 7 + index / 251);
        return bytes;
    }

    [Fact]
    public void Chunks_AreReadBackOnlyOnceKept()
    {
        var content = Content(Chunk * 2 + 1000);
        using var file = Cache().OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!;

        Assert.Equal(3, file.ChunkCount);
        var buffer = new byte[Chunk];
        Assert.Equal(0, file.Read(Chunk, buffer));

        file.WriteChunk(1, content.AsSpan(Chunk, Chunk));
        Assert.True(file.HasChunk(1));
        Assert.False(file.HasChunk(0));
        Assert.Equal(0, file.Read(10, buffer));

        // A read ends with its chunk
        Assert.Equal(Chunk - 100, file.Read(Chunk + 100, buffer));
        Assert.Equal(content.AsSpan(Chunk + 100, Chunk - 100).ToArray(), buffer.AsSpan(0, Chunk - 100).ToArray());
        Assert.Null(file.CompletePath);
    }

    [Fact]
    public void LastChunk_CompletesTheFileAsAnOrdinaryCachedAttachment()
    {
        var cache = Cache();
        var content = Content(Chunk + 10);
        using var file = cache.OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!;

        file.WriteChunk(1, content.AsSpan(Chunk));
        Assert.Null(cache.FindFile("coordinator", 5, "clip.mp4"));
        file.WriteChunk(0, content.AsSpan(0, Chunk));

        var path = cache.FindFile("coordinator", 5, "clip.mp4");
        Assert.NotNull(path);
        Assert.Equal(path, file.CompletePath);
        Assert.Equal(content, File.ReadAllBytes(path));

        // Still readable after the move
        var buffer = new byte[10];
        Assert.Equal(10, file.Read(Chunk, buffer));
        Assert.Equal(content.AsSpan(Chunk).ToArray(), buffer);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.stream*", SearchOption.AllDirectories));
    }

    [Fact]
    public void KeptChunks_SurviveReopening()
    {
        var content = Content(Chunk * 3);
        using (var file = Cache().OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!)
            file.WriteChunk(2, content.AsSpan(Chunk * 2));

        using var reopened = Cache().OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!;
        Assert.Equal(1, reopened.ChunksPresent);
        Assert.True(reopened.HasChunk(2));
        var buffer = new byte[Chunk];
        Assert.Equal(Chunk, reopened.Read(Chunk * 2, buffer));
        Assert.Equal(content.AsSpan(Chunk * 2).ToArray(), buffer);
    }

    [Fact]
    public void KeptChunks_OfAnotherSize_AreDropped()
    {
        var content = Content(Chunk * 3);
        using (var file = Cache().OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!)
            file.WriteChunk(0, content.AsSpan(0, Chunk));

        using var reopened = Cache().OpenPartialFile("coordinator", 5, "clip.mp4", content.Length + 1)!;
        Assert.Equal(0, reopened.ChunksPresent);
    }

    [Fact]
    public void SameAttachment_IsSharedUntilTheLastCallerLetsGo()
    {
        var cache = Cache();
        var content = Content(Chunk * 2);
        var first = cache.OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!;
        using var second = cache.OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!;

        first.WriteChunk(0, content.AsSpan(0, Chunk));
        Assert.True(second.HasChunk(0));
        first.Dispose();
        first.Dispose();

        second.WriteChunk(1, content.AsSpan(Chunk));
        Assert.NotNull(second.CompletePath);
    }

    [Fact]
    public void OpenPartialFile_IsNotTrimmedWhileOpen()
    {
        var cache = Cache();
        cache.SizeLimit = 1;
        var content = Content(Chunk * 2);
        using var file = cache.OpenPartialFile("coordinator", 5, "clip.mp4", content.Length)!;
        file.WriteChunk(0, content.AsSpan(0, Chunk));

        cache.Trim();
        Assert.True(file.HasChunk(0));
        file.Dispose();

        cache.Trim();
        Assert.Empty(Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories));
    }
}
