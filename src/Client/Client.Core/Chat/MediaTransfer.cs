namespace Client.Core.Chat;

public enum MediaUploadKind
{
    Image,
    ImagePreview,
    /// <summary>A picture for the profile of the user; it belongs to no chat.</summary>
    Avatar
}

/// <summary>
/// A file to put on the server before the message that carries it is sent.
/// </summary>
/// <param name="ChatId">The chat of that message; null for an avatar.</param>
/// <param name="OpenContent">
/// Opens the bytes from their start. Called again when an interrupted upload is continued,
/// so the source does not have to be seekable.
/// </param>
/// <param name="ResumeUploadId">An earlier upload of the same bytes to continue instead of starting over.</param>
public record MediaUpload(
    int? ChatId,
    MediaUploadKind Kind,
    string MimeType,
    long Size,
    Func<Task<Stream>> OpenContent,
    string? ResumeUploadId = null);

public enum MediaTransferResult
{
    NoError,
    Canceled,
    /// <summary>The server could not be reached, also after the retries.</summary>
    ConnectionFailed,
    Unauthorized,
    /// <summary>The image, the chat or the upload being continued does not exist anymore.</summary>
    NotFound,
    /// <summary>The chat does not take attachments.</summary>
    NotAllowed,
    TooLarge,
    TypeNotAllowed,
    /// <summary>Too many uploads of this user wait to be attached to a message.</summary>
    TooManyUploads,
    StorageFull,
    Failed
}

/// <param name="UploadId">
/// Set once the server knows the upload, also when it did not finish: it can be continued then.
/// </param>
public readonly record struct MediaUploadOutcome(MediaTransferResult Result, string? UploadId);
