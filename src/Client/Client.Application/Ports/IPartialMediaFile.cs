namespace Client.Application.Ports;

/// <summary>
/// An attachment kept in the media cache while it is downloaded piece by piece, so it can be played before it is
/// all there. The file is split into chunks of <see cref="ChunkSize"/> bytes, and only whole chunks are kept. Once
/// every chunk is in, the file becomes an ordinary cached attachment at <see cref="CompletePath"/>.
/// Safe to use from several threads.
/// </summary>
public interface IPartialMediaFile : IDisposable
{
    /// <summary>The size of the whole file.</summary>
    long Length { get; }

    int ChunkSize { get; }

    int ChunkCount { get; }

    /// <summary>How many chunks are kept.</summary>
    int ChunksPresent { get; }

    /// <summary>The cached attachment, once every chunk is in.</summary>
    string? CompletePath { get; }

    bool HasChunk(int chunk);

    /// <summary>
    /// Reads from <paramref name="position"/> on, up to the end of the chunk it is in.
    /// </summary>
    /// <returns>How many bytes were read: 0 when the chunk is not kept or the position is past the end.</returns>
    int Read(long position, Span<byte> buffer);

    /// <summary>
    /// Keeps a chunk; <paramref name="data"/> is all of it, which is <see cref="ChunkSize"/> bytes except for the
    /// last one. A chunk that is already kept stays as it is.
    /// </summary>
    void WriteChunk(int chunk, ReadOnlySpan<byte> data);
}
