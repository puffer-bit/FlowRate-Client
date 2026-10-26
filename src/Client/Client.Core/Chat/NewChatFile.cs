namespace Client.Core.Chat;

/// <summary>
/// A file of a message being sent, which is downloaded rather than shown. Its bytes are on the server already.
/// </summary>
public class NewChatFile
{
    public required string UploadId { get; init; }
    public required string FileName { get; init; }
}
