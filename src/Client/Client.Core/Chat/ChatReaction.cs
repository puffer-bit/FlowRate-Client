namespace Client.Core.Chat;

public class ChatReaction
{
    public required int UserId { get; init; }
    public required string Emoji { get; init; }
}