namespace Client.Core.Chat;

/// <summary>
/// An image of a message being sent. Its bytes are on the server already, as two uploads:
/// the image itself and its reduced copy.
/// </summary>
public class NewChatImage
{
    public required string UploadId { get; init; }
    public required string PreviewUploadId { get; init; }
    public required string FileName { get; init; }
    public string? Description { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }
}
