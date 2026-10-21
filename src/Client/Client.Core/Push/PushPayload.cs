namespace Client.Core.Push;

public enum PushAttachmentKind
{
    None = 0,
    Image = 1,
    Video = 2,
    File = 3
}

/// <summary>
/// What a push notification about a new chat message carries once it is decrypted.
/// </summary>
/// <param name="SenderAvatarUrl">The address of the avatar of the sender, or null when there is none.</param>
/// <param name="Text">The beginning of the text, or null for a message of attachments only.</param>
/// <param name="Attachment">The first kind of attachment the message has.</param>
/// <param name="SentTime">Unix time in milliseconds.</param>
/// <param name="Mention">The message mentions the receiver: it is notified about also in a muted chat.</param>
public sealed record PushPayload(
    string CoordinatorId,
    int ChatId,
    string ChatName,
    int MessageId,
    int SenderId,
    string SenderName,
    string? Text,
    PushAttachmentKind Attachment,
    long SentTime,
    string? SenderAvatarUrl = null,
    bool Mention = false);
