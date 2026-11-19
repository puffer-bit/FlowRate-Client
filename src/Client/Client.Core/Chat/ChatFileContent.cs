namespace Client.Core.Chat;

public class ChatFileContent
{
    public required int Id { get; init; }
    public required string FileName { get; init; }
    public required long Size { get; init; }
    public required string MimeType { get; init; }

    /// <summary>Set for a file the server knows to be a video; null for others and for those sent before.</summary>
    public ChatFileVideo? Video { get; init; }
}

/// <summary>What a video is shown with before any of it is downloaded.</summary>
/// <param name="Width">The sides of its picture as shown; 0 when unknown.</param>
/// <param name="Duration">Null when unknown.</param>
/// <param name="HasPreview">The server keeps a frame of it to show.</param>
public sealed record ChatFileVideo(int Width, int Height, TimeSpan? Duration, bool HasPreview);
