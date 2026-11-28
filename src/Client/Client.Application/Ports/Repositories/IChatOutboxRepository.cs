namespace Client.Application.Ports.Repositories;

/// <summary>
/// The messages sent and not yet taken by the server, kept so that they still go after a restart of the
/// client. Keyed by the id the client gave each message.
/// </summary>
public interface IChatOutboxRepository
{
    /// <summary>The waiting messages of the coordinator, oldest first, each with its files in order.</summary>
    Task<IReadOnlyList<StoredOutgoingMessage>> GetAllAsync(string coordinatorInstanceId);

    /// <summary>Adds a message or replaces the one with its id, files included.</summary>
    Task SaveAsync(StoredOutgoingMessage message);

    /// <summary>Forgets a message once it was sent or given up; nothing happens for an unknown id.</summary>
    Task DeleteAsync(Guid clientMessageId);
}

/// <summary>A message waiting to be sent, as it is stored.</summary>
/// <param name="ReplyToMessageId">The message it answers, with what its quote shows.</param>
public sealed record StoredOutgoingMessage(
    Guid ClientMessageId,
    string CoordinatorInstanceId,
    int ChatId,
    DateTimeOffset CreatedAt,
    string? Text,
    bool IsLinkPreviewDisabled,
    int? ReplyToMessageId,
    int? ReplySenderUserId,
    string? ReplyText,
    bool ReplyHasAttachments,
    IReadOnlyList<StoredOutgoingFile> Files);

/// <summary>
/// A file of a waiting message: where it is read from again and what was chosen for it. Uploads are not kept,
/// the file goes again from its start.
/// </summary>
/// <param name="Path">A path of this device; null when the file cannot be opened again, such as one the platform
/// handed over only for a while.</param>
public sealed record StoredOutgoingFile(
    string Name,
    string? Path,
    long Size,
    bool IsImage,
    bool IsCompressed,
    bool IsMetadataStripped,
    int? VideoSide);
