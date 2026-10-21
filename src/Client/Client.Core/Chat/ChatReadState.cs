namespace Client.Core.Chat;

/// <summary>
/// Where the user has read a chat up to, kept by the server for all devices of the user.
/// The counts cover the unread messages of others and stop at <see cref="MaxCount"/>.
/// <paramref name="MentionCount"/> counts the ones that mention the user by id or with @everyone.
/// </summary>
public sealed record ChatReadState(int ChatId, int LastReadMessageId, int UnreadCount, int MentionCount)
{
    public const int MaxCount = 100;
}
