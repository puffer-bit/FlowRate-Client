namespace Client.Core.Chat;

/// <summary>
/// Where this device has read a chat up to, stored locally for each coordinator and account.
/// The counts cover the unread messages of others and stop at <see cref="MaxCount"/>.
/// <paramref name="MentionCount"/> counts the ones that mention the user by id or with @everyone.
/// <paramref name="PeerReadMessageId"/> comes from the server only: the furthest position of the other
/// members of a direct chat or a group, 0 elsewhere.
/// </summary>
public sealed record ChatReadState(int ChatId, int LastReadMessageId, int UnreadCount, int MentionCount,
    int PeerReadMessageId = 0)
{
    public const int MaxCount = 100;
}
