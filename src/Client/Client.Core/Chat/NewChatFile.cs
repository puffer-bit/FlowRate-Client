namespace Client.Core.Chat;

/// <summary>
/// A file of a message being sent, which is downloaded rather than shown. Its bytes are on the server already.
/// </summary>
public class NewChatFile
{
    public required string UploadId { get; init; }
    public required string FileName { get; init; }

    /// <summary>The upload of the frame a video is shown with; null for other files, or a video without one.</summary>
    public string? PreviewUploadId { get; init; }
}
