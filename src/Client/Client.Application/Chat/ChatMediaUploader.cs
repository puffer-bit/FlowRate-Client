using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;
using Shared.Validation;

namespace Client.Application.Chat;

/// <summary>
/// A picked file as the platform hands it over.
/// </summary>
/// <param name="Open">Opens the bytes from their start; may be called several times.</param>
public record ChatImageSource(string FileName, long Size, Func<Task<Stream>> Open);

/// <summary>
/// What is known about an image being uploaded. Kept by the caller between attempts, so a retry
/// continues where the last one stopped instead of sending the bytes again.
/// </summary>
public class ChatImageUploadState
{
    internal ImagePreview? Preview { get; set; }
    internal string? PreviewUploadId { get; set; }
    internal bool IsPreviewUploaded { get; set; }
    internal string? UploadId { get; set; }

    /// <summary>Set once both uploads are complete.</summary>
    public NewChatImage? Image { get; internal set; }
}

public enum ChatImageUploadResult
{
    NoError,
    Canceled,
    /// <summary>The file is not an image the chat can carry.</summary>
    NotAnImage,
    ConnectionFailed,
    NotAllowed,
    TooLarge,
    TooManyUploads,
    StorageFull,
    Failed
}

public interface IChatMediaUploader
{
    /// <summary>
    /// Puts an image and its reduced copy on the server, ready to be named by a message.
    /// </summary>
    /// <param name="progress">The share of the image the server holds, from 0 to 1.</param>
    Task<ChatImageUploadResult> UploadImageAsync(ICoordinatorSession session, int chatId, ChatImageSource source,
        ChatImageUploadState state, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public class ChatMediaUploader : IChatMediaUploader
{
    private readonly IImagePreviewer _previewer;

    public ChatMediaUploader(IImagePreviewer previewer)
    {
        _previewer = previewer;
    }

    public async Task<ChatImageUploadResult> UploadImageAsync(ICoordinatorSession session, int chatId,
        ChatImageSource source, ChatImageUploadState state, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (state.Image != null)
            return ChatImageUploadResult.NoError;

        try
        {
            state.Preview ??= await _previewer.CreateAsync(source.Open, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ChatImageUploadResult.Canceled;
        }

        if (state.Preview is not { } preview || !Limits.ChatImageMimeTypes.Contains(preview.MimeType))
            return ChatImageUploadResult.NotAnImage;

        // The reduced copy goes first: it is small, and the server refuses here what it would refuse anyway
        if (!state.IsPreviewUploaded)
        {
            var outcome = await UploadAsync(session, new MediaUpload(chatId, MediaUploadKind.ImagePreview,
                    preview.PreviewMimeType, preview.Preview.Length,
                    () => Task.FromResult<Stream>(new MemoryStream(preview.Preview, writable: false)),
                    state.PreviewUploadId),
                null, id => state.PreviewUploadId = id, cancellationToken);
            if (outcome != MediaTransferResult.NoError)
                return ResultOf(outcome);

            state.IsPreviewUploaded = true;
        }

        var size = Math.Max(source.Size, 1);
        var result = await UploadAsync(session, new MediaUpload(chatId, MediaUploadKind.Image, preview.MimeType,
                source.Size, source.Open, state.UploadId),
            progress == null ? null : new Progress(progress, size), id => state.UploadId = id, cancellationToken);
        if (result != MediaTransferResult.NoError)
            return ResultOf(result);

        state.Image = new NewChatImage
        {
            UploadId = state.UploadId!,
            PreviewUploadId = state.PreviewUploadId!,
            FileName = FileNameOf(source.FileName),
            Width = preview.Width,
            Height = preview.Height
        };
        progress?.Report(1);
        return ChatImageUploadResult.NoError;
    }

    private static async Task<MediaTransferResult> UploadAsync(ICoordinatorSession session, MediaUpload upload,
        IProgress<long>? progress, Action<string?> remember, CancellationToken cancellationToken)
    {
        var outcome = await session.UploadMedia(upload, progress, cancellationToken);

        // An upload the server has dropped cannot be continued; the next attempt starts a new one
        remember(outcome.Result == MediaTransferResult.NotFound ? null : outcome.UploadId);
        if (outcome.Result != MediaTransferResult.NotFound || upload.ResumeUploadId == null)
            return outcome.Result;

        outcome = await session.UploadMedia(upload with { ResumeUploadId = null }, progress, cancellationToken);
        remember(outcome.UploadId);
        return outcome.Result;
    }

    /// <summary>
    /// The name the server accepts: without the control characters a file system may allow, and not
    /// longer than its limit, keeping the extension.
    /// </summary>
    private static string FileNameOf(string name)
    {
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
            return "image";
        if (cleaned.Length <= Limits.FileNameMaxLength)
            return cleaned;

        var extension = Path.GetExtension(cleaned);
        return extension.Length is > 0 and < 16
            ? cleaned[..(Limits.FileNameMaxLength - extension.Length)] + extension
            : cleaned[..Limits.FileNameMaxLength];
    }

    private static ChatImageUploadResult ResultOf(MediaTransferResult result) => result switch
    {
        MediaTransferResult.NoError => ChatImageUploadResult.NoError,
        MediaTransferResult.Canceled => ChatImageUploadResult.Canceled,
        MediaTransferResult.ConnectionFailed or MediaTransferResult.Unauthorized => ChatImageUploadResult.ConnectionFailed,
        MediaTransferResult.NotAllowed => ChatImageUploadResult.NotAllowed,
        MediaTransferResult.TooLarge => ChatImageUploadResult.TooLarge,
        MediaTransferResult.TypeNotAllowed => ChatImageUploadResult.NotAnImage,
        MediaTransferResult.TooManyUploads => ChatImageUploadResult.TooManyUploads,
        MediaTransferResult.StorageFull => ChatImageUploadResult.StorageFull,
        _ => ChatImageUploadResult.Failed
    };

    private sealed class Progress(IProgress<double> target, long size) : IProgress<long>
    {
        public void Report(long value) => target.Report(Math.Clamp((double)value / size, 0, 1));
    }
}
