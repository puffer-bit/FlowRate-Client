using System;
using Client.Application.Chat;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// What a player opens: the attachment kept whole in the cache, or the attachment as it downloads. Disposing it
/// stops the download; what arrived stays in the cache.
/// </summary>
public sealed class ChatPlaybackSource : IDisposable
{
    private ChatPlaybackSource(string? path, StreamingDownload? download, string fileName)
    {
        Path = path;
        Download = download;
        FileName = fileName;
    }

    /// <summary>The name of the attachment, whose extension tells a player its container.</summary>
    public string FileName { get; }

    /// <summary>The cached file, when the attachment was all there already.</summary>
    public string? Path { get; }

    /// <summary>The attachment as it downloads, when it was not all there.</summary>
    public StreamingDownload? Download { get; }

    public static ChatPlaybackSource FromFile(string path, string fileName) => new(path, null, fileName);

    public static ChatPlaybackSource FromDownload(StreamingDownload download, string fileName) =>
        new(null, download, fileName);

    public void Dispose() => Download?.Dispose();
}
