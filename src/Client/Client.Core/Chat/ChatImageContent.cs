namespace Client.Core.Chat;

public class ChatImageContent
{
    public required int Id { get; init; }
    public required string FileName { get; init; }
    public string? Thumbnail { get; init; }
    public string? Description { get; init; }
    public required long Size { get; init; }
    public required string MimeType { get; init; }
    
    public required int Width { get; init; }
    public required int Height { get; init; }
}