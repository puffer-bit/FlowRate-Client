using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;
using Shared.Validation;

namespace Client.Application.Chat;

/// <summary>
/// A picked file as the platform hands it over.
/// </summary>
/// <param name="Open">Opens the bytes from their start; may be called several times.</param>
/// <param name="LocalPath">The file on this disk, where the platform gives one: read in place by what cannot take a stream.</param>
public record ChatMediaSource(string FileName, long Size, Func<Task<Stream>> Open, string? LocalPath = null);

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

/// <summary>
/// What is known about a file being uploaded, kept by the caller between attempts like
/// <see cref="ChatImageUploadState"/>.
/// </summary>
public class ChatFileUploadState
{
    internal string? UploadId { get; set; }
    internal bool IsUploaded { get; set; }

    // The frame a video is shown with: made once, then uploaded once
    internal bool IsFrameTaken { get; set; }
    internal ImagePreview? Frame { get; set; }
    internal string? FrameUploadId { get; set; }
    internal bool IsFrameUploaded { get; set; }

    /// <summary>Set once the upload is complete.</summary>
    public NewChatFile? File { get; internal set; }
}

public enum ChatMediaUploadResult
{
    NoError,
    Canceled,
    /// <summary>The file is not an image the chat can carry.</summary>
    NotAnImage,
    /// <summary>The file has no bytes: the server takes none such.</summary>
    Empty,
    ConnectionFailed,
    NotAllowed,
    TooLarge,
    TooManyUploads,
    StorageFull,
    /// <summary>The file could not be read here: gone, locked by another program, or not ours to read.</summary>
    Unreadable,
    Failed
}

public interface IChatMediaUploader
{
    /// <summary>
    /// Puts an image and its reduced copy on the server, ready to be named by a message.
    /// </summary>
    /// <param name="progress">The share of the image the server holds, from 0 to 1.</param>
    Task<ChatMediaUploadResult> UploadImageAsync(ICoordinatorSession session, int chatId, ChatMediaSource source,
        ChatImageUploadState state, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a file of any type on the server as it is, ready to be named by a message.
    /// </summary>
    /// <param name="progress">The share of the file the server holds, from 0 to 1.</param>
    Task<ChatMediaUploadResult> UploadFileAsync(ICoordinatorSession session, int chatId, ChatMediaSource source,
        ChatFileUploadState state, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public class ChatMediaUploader : IChatMediaUploader
{
    private readonly IImagePreviewer _previewer;

    // Absent where the platform cannot decode videos: they go without a frame
    private readonly IVideoFrameGrabber? _frames;

    // The longer side of the frame a video is shown with, as of the reduced copy of an image
    private const int FrameSide = 640;

    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".webm", ".mov", ".avi" };

    public ChatMediaUploader(IImagePreviewer previewer, IVideoFrameGrabber? frames = null)
    {
        _previewer = previewer;
        _frames = frames;
    }

    public async Task<ChatMediaUploadResult> UploadImageAsync(ICoordinatorSession session, int chatId,
        ChatMediaSource source, ChatImageUploadState state, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (state.Image != null)
            return ChatMediaUploadResult.NoError;

        try
        {
            state.Preview ??= await _previewer.CreateAsync(source.Open, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ChatMediaUploadResult.Canceled;
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return ChatMediaUploadResult.Unreadable;
        }

        if (state.Preview is not { } preview || !Limits.ChatImageMimeTypes.Contains(preview.MimeType))
            return ChatMediaUploadResult.NotAnImage;

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
        MediaTransferResult result;
        try
        {
            result = await UploadAsync(session, new MediaUpload(chatId, MediaUploadKind.Image, preview.MimeType,
                    source.Size, source.Open, state.UploadId),
                progress == null ? null : new Progress(progress, size), id => state.UploadId = id, cancellationToken);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return ChatMediaUploadResult.Unreadable;
        }

        if (result != MediaTransferResult.NoError)
            return ResultOf(result);

        state.Image = new NewChatImage
        {
            UploadId = state.UploadId!,
            PreviewUploadId = state.PreviewUploadId!,
            FileName = FileNameOf(source.FileName, "image"),
            Width = preview.Width,
            Height = preview.Height
        };
        progress?.Report(1);
        return ChatMediaUploadResult.NoError;
    }

    public async Task<ChatMediaUploadResult> UploadFileAsync(ICoordinatorSession session, int chatId,
        ChatMediaSource source, ChatFileUploadState state, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (state.File != null)
            return ChatMediaUploadResult.NoError;

        // The server takes no empty upload, and an empty file says nothing anyway
        if (source.Size <= 0)
            return ChatMediaUploadResult.Empty;

        if (!state.IsUploaded)
        {
            MediaTransferResult result;
            try
            {
                result = await UploadAsync(session, new MediaUpload(chatId, MediaUploadKind.File,
                        FileMimeTypes.Of(source.FileName), source.Size, source.Open, state.UploadId),
                    progress == null ? null : new Progress(progress, source.Size), id => state.UploadId = id,
                    cancellationToken);
            }
            catch (Exception exception) when (IsUnreadable(exception))
            {
                return ChatMediaUploadResult.Unreadable;
            }

            if (result != MediaTransferResult.NoError)
                return ResultOf(result);
            state.IsUploaded = true;
        }

        var frame = await UploadFrameAsync(session, chatId, source, state, cancellationToken);
        if (frame is not (MediaTransferResult.NoError or MediaTransferResult.NotFound))
            return ResultOf(frame);

        state.File = new NewChatFile
        {
            UploadId = state.UploadId!,
            FileName = FileNameOf(source.FileName, "file"),
            PreviewUploadId = state.IsFrameUploaded ? state.FrameUploadId : null
        };
        progress?.Report(1);
        return ChatMediaUploadResult.NoError;
    }

    /// <summary>
    /// Puts the frame a video is shown with on the server. A video goes without one when no frame can be
    /// had or the server does not take it; only a broken connection or a cancel holds the file back.
    /// </summary>
    /// <returns><see cref="MediaTransferResult.NotFound"/> when the video goes without a frame.</returns>
    private async Task<MediaTransferResult> UploadFrameAsync(ICoordinatorSession session, int chatId,
        ChatMediaSource source, ChatFileUploadState state, CancellationToken cancellationToken)
    {
        if (state.IsFrameUploaded)
            return MediaTransferResult.NoError;

        if (!state.IsFrameTaken)
        {
            if (_frames is not { IsAvailable: true } frames || source.LocalPath is not { } path
                || !VideoExtensions.Contains(Path.GetExtension(source.FileName)))
                return MediaTransferResult.NotFound;

            try
            {
                var image = await frames.GrabAsync(path, FrameSide, cancellationToken);
                state.Frame = image == null ? null : await _previewer.CreateAsync(image, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return MediaTransferResult.Canceled;
            }
            state.IsFrameTaken = true;
        }

        if (state.Frame is not { } frame || frame.Preview.Length > Limits.ChatImagePreviewMaxSize)
            return MediaTransferResult.NotFound;

        var outcome = await UploadAsync(session, new MediaUpload(chatId, MediaUploadKind.ImagePreview,
                frame.PreviewMimeType, frame.Preview.Length,
                () => Task.FromResult<Stream>(new MemoryStream(frame.Preview, writable: false)), state.FrameUploadId),
            null, id => state.FrameUploadId = id, cancellationToken);
        switch (outcome)
        {
            case MediaTransferResult.NoError:
                state.IsFrameUploaded = true;
                return outcome;
            case MediaTransferResult.Canceled or MediaTransferResult.ConnectionFailed or MediaTransferResult.Unauthorized:
                return outcome;
            default:
                // Not taken: the video is sent as it was before frames
                state.Frame = null;
                return MediaTransferResult.NotFound;
        }
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
    private static string FileNameOf(string name, string fallback)
    {
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
            return fallback;
        if (cleaned.Length <= Limits.FileNameMaxLength)
            return cleaned;

        var extension = Path.GetExtension(cleaned);
        return extension.Length is > 0 and < 16
            ? cleaned[..(Limits.FileNameMaxLength - extension.Length)] + extension
            : cleaned[..Limits.FileNameMaxLength];
    }

    // Reading the picked file failed, as Windows refuses a file another program writes to without
    // sharing it; the transfer itself reports its network failures as results
    private static bool IsUnreadable(Exception exception) => exception is IOException or UnauthorizedAccessException;

    private static ChatMediaUploadResult ResultOf(MediaTransferResult result) => result switch
    {
        MediaTransferResult.NoError => ChatMediaUploadResult.NoError,
        MediaTransferResult.Canceled => ChatMediaUploadResult.Canceled,
        MediaTransferResult.ConnectionFailed or MediaTransferResult.Unauthorized => ChatMediaUploadResult.ConnectionFailed,
        MediaTransferResult.NotAllowed => ChatMediaUploadResult.NotAllowed,
        MediaTransferResult.TooLarge => ChatMediaUploadResult.TooLarge,
        MediaTransferResult.TypeNotAllowed => ChatMediaUploadResult.NotAnImage,
        MediaTransferResult.TooManyUploads => ChatMediaUploadResult.TooManyUploads,
        MediaTransferResult.StorageFull => ChatMediaUploadResult.StorageFull,
        _ => ChatMediaUploadResult.Failed
    };

    private sealed class Progress(IProgress<double> target, long size) : IProgress<long>
    {
        public void Report(long value) => target.Report(Math.Clamp((double)value / size, 0, 1));
    }
}
