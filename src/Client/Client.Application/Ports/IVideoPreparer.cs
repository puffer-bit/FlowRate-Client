using Client.Application.Chat;
using Client.Core.Chat;

namespace Client.Application.Ports;

/// <summary>What is done to a video before it is sent.</summary>
/// <param name="Compress">Reduced to <paramref name="Preset"/> where it is above it.</param>
/// <param name="StripMetadata">Without where and when it was taken and by what; the rotation stays.</param>
public sealed record VideoPreparation(bool Compress, bool StripMetadata, VideoCompressionPreset Preset);

/// <summary>
/// A video as it will be sent: a new file, or the original as it is. The new file is deleted with it.
/// </summary>
public sealed class PreparedVideo(VideoFacts facts, VideoPreparationKind kind, string? path, string fileName, long size)
    : IDisposable
{
    /// <summary>Of the original.</summary>
    public VideoFacts Facts { get; } = facts;

    public VideoPreparationKind Kind { get; } = kind;

    /// <summary>The file to send; null to send the original as it is.</summary>
    public string? Path { get; } = path;

    /// <summary>The name it goes under, with the extension of its container.</summary>
    public string FileName { get; } = fileName;

    public long Size { get; } = size;

    public void Dispose()
    {
        if (Path == null)
            return;
        try { File.Delete(Path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public interface IVideoPreparer
{
    /// <summary>The media runtime is loaded and has what compressing takes.</summary>
    bool IsAvailable { get; }

    /// <param name="progress">From 0 to 1 while a new file is written.</param>
    /// <returns>Null when the content is not a video this can read.</returns>
    /// <exception cref="InvalidOperationException">The video could not be written again.</exception>
    Task<PreparedVideo?> PrepareAsync(ChatMediaSource source, VideoPreparation preparation,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
