namespace Client.Core.Chat;

public class ChatFileContent
{
    public required int Id { get; init; }
    public required string FileName { get; init; }
    public required long Size { get; init; }
    public required string MimeType { get; init; }
}