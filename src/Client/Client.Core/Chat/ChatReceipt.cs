namespace Client.Core.Chat;

/// <summary>
/// Another member of a direct chat or a group read it up to <paramref name="LastReadMessageId"/> at
/// <paramref name="ReadAt"/> (UTC).
/// </summary>
public sealed record ChatPeerRead(int ChatId, int ReaderUserId, int LastReadMessageId, DateTime ReadAt);

/// <summary>A member who read a message; <paramref name="ReadAt"/> (UTC) is null when the time is unknown.</summary>
public sealed record ChatMessageReader(int UserId, DateTime? ReadAt);

/// <summary>What the author sees of an own message in a direct chat or a group.</summary>
public enum ChatDeliveryStatus
{
    /// <summary>Not an own message, or a chat without receipts.</summary>
    None,
    /// <summary>On the server, nobody else has read it yet.</summary>
    Sent,
    /// <summary>Read by at least one other member.</summary>
    Read
}
