namespace Client.Core.Chat;

public class ChatReaction
{
    public required int UserId { get; init; }
    public required string Emoji { get; init; }

    /// <summary>UTC; null for reactions set before the server kept the time.</summary>
    public DateTime? ReactedAt { get; init; }
}