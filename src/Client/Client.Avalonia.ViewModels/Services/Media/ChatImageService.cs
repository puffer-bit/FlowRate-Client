using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Avalonia.Controls.Images;
using Client.Core.Chat;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Client.Services;

public sealed partial class ChatImageService : IChatImageService
{
    // The longer side of the reduced copy a message shows; the original is fetched to see more
    private const int PreviewSide = 640;
    private const int PreviewQuality = 80;

    private readonly IMediaCache _cache;
    private readonly ILogger<ChatImageService> _logger;

    // One download per image at a time, however many views ask for it
    private readonly ConcurrentDictionary<(string Coordinator, int ImageId, bool Preview), Task<(MediaTransferResult, string?)>>
        _fetches = new();

    public ChatImageService(IMediaCache cache, ILogger<ChatImageService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<ImagePreview?> CreateAsync(Func<Task<Stream>> openContent, CancellationToken cancellationToken = default) =>
        ImageDecoding.RunAsync(async () =>
        {
            try
            {
                await using var content = await OpenSeekableAsync(openContent, cancellationToken);
                using var decoded = SkiaImages.Decode(content, PreviewSide);
                if (decoded == null || SkiaImages.MimeTypeOf(decoded.Format) is not { } mimeType)
                    return null;

                using var encoded = decoded.Bitmap.Encode(SKEncodedImageFormat.Webp, PreviewQuality);
                return encoded == null
                    ? null
                    : new ImagePreview(mimeType, decoded.Width, decoded.Height, encoded.ToArray(), "image/webp");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDecodeFailed(exception);
                return null;
            }
        }, cancellationToken);

    public async Task<Bitmap?> LoadPreviewAsync(ICoordinatorSession session, int imageId)
    {
        var (_, path) = await FetchAsync(session, imageId, preview: true, null, CancellationToken.None);
        return path == null ? null : await DecodeStillAsync(path);
    }

    public async Task<IImage?> LoadAnimationAsync(ICoordinatorSession session, int imageId)
    {
        var (_, path) = await FetchAsync(session, imageId, preview: false, null, CancellationToken.None);
        return path == null
            ? null
            : await DecodeAsync(() => Task.FromResult<Stream>(File.OpenRead(path)), PreviewSide) as AnimatedImage;
    }

    public Task<(MediaTransferResult Result, string? Path)> FetchOriginalAsync(ICoordinatorSession session,
        int imageId, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        FetchAsync(session, imageId, preview: false, progress, cancellationToken);

    public Task<IImage?> DecodeAsync(Func<Task<Stream>> openContent, int maxSide) =>
        ImageDecoding.RunAsync(async () =>
        {
            try
            {
                await using var content = await OpenSeekableAsync(openContent, CancellationToken.None);
                return Pictures.Decode(content, maxSide);
            }
            catch (Exception exception)
            {
                LogDecodeFailed(exception);
                return null;
            }
        });

    // The reduced copy is a still WebP: there is nothing to animate in it
    private Task<Bitmap?> DecodeStillAsync(string path) =>
        ImageDecoding.RunAsync(() =>
        {
            try
            {
                using var content = File.OpenRead(path);
                using var decoded = SkiaImages.Decode(content, PreviewSide);
                return decoded == null ? null : Pictures.ToBitmap(decoded.Bitmap);
            }
            catch (Exception exception)
            {
                LogDecodeFailed(exception);
                return null;
            }
        });

    private async Task<(MediaTransferResult Result, string? Path)> FetchAsync(ICoordinatorSession session, int imageId,
        bool preview, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var coordinator = session.GetCurrentUser().CoordinatorInstanceId;
        if (await Task.Run(() => _cache.Find(coordinator, imageId, preview), cancellationToken) is { } kept)
            return (MediaTransferResult.NoError, kept);

        var key = (coordinator, imageId, preview);
        while (true)
        {
            var created = new TaskCompletionSource<(MediaTransferResult, string?)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fetch = _fetches.GetOrAdd(key, created.Task);
            if (fetch != created.Task)
            {
                // Someone else downloads it; a download that was canceled there is started again here
                var (result, path) = await fetch.WaitAsync(cancellationToken);
                if (result != MediaTransferResult.Canceled)
                    return (result, path);
                continue;
            }

            try
            {
                var result = MediaTransferResult.Failed;
                var path = await _cache.StoreAsync(coordinator, imageId, preview, async stream =>
                {
                    result = await session.DownloadChatImage(imageId, preview, stream, progress, cancellationToken);
                    return result == MediaTransferResult.NoError;
                });

                var outcome = (path == null && result == MediaTransferResult.NoError ? MediaTransferResult.Failed : result, path);
                created.SetResult(outcome);
                return outcome;
            }
            catch (Exception exception)
            {
                created.SetException(exception);
                throw;
            }
            finally
            {
                _fetches.TryRemove(new(key, created.Task));
            }
        }
    }

    /// <summary>
    /// The decoder goes back and forth in the content; a source that cannot is read into memory first.
    /// </summary>
    private static async Task<Stream> OpenSeekableAsync(Func<Task<Stream>> openContent, CancellationToken cancellationToken)
    {
        var content = await openContent();
        if (content.CanSeek)
            return content;

        await using (content)
        {
            var buffered = new MemoryStream();
            await content.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            return buffered;
        }
    }

    [LoggerMessage(LogLevel.Debug, "An image could not be decoded")]
    private partial void LogDecodeFailed(Exception exception);
}
