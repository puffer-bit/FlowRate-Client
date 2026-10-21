using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Client.Avalonia.Controls.Avatars;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.Core.Users;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Client.Services;

/// <summary>
/// A picture picked for an avatar, made as small as an avatar is shown.
/// </summary>
/// <param name="Bitmap">What the dialog shows until the picture is saved; owned by the receiver.</param>
/// <param name="Encoded">What goes to the server.</param>
public sealed record PreparedAvatar(Bitmap Bitmap, byte[] Encoded, string MimeType);

/// <summary>
/// The avatars of users as the interface needs them: fetched from the coordinator once, kept in
/// the media cache and shared by every place that shows the same user.
/// </summary>
public interface IAvatarService
{
    /// <summary>
    /// The session avatars are downloaded through, or null when there is none. Avatars kept from
    /// earlier are still shown without it.
    /// </summary>
    void Attach(ICoordinatorSession? session);

    /// <summary>
    /// The picture at <see cref="UserModel.AvatarUrl"/>, or null when there is none or it cannot be had now.
    /// </summary>
    /// <param name="coordinatorInstanceId">Null means the coordinator of the attached session.</param>
    Task<Bitmap?> LoadAsync(string? coordinatorInstanceId, string? avatarUrl);

    /// <summary>
    /// Cuts the middle square out of a picked image and reduces it. Null when it is not an image.
    /// </summary>
    Task<PreparedAvatar?> PrepareAsync(Func<Task<Stream>> openContent);
}

public sealed partial class AvatarService : IAvatarService
{
    // Enough for the largest avatar, 96 px, on a high density screen
    private const int Side = 256;
    private const int Quality = 85;
    private const string MimeType = "image/webp";

    // A picked image is decoded to at most this before its middle is cut out, so a long one keeps a sharp middle
    private const int PickedMaxSide = 2048;

    private readonly IMediaCache _cache;
    private readonly ILogger<AvatarService> _logger;

    // A bitmap lives as long as something shows it; the next one to ask decodes the kept file again
    private readonly ConcurrentDictionary<(string Coordinator, string AvatarId), WeakReference<Bitmap>> _bitmaps = new();

    // One download and one decoding per avatar at a time, however many views ask for it
    private readonly ConcurrentDictionary<(string Coordinator, string AvatarId), Task<Bitmap?>> _loads = new();

    private ICoordinatorSession? _session;
    private string? _coordinator;

    public AvatarService(IMediaCache cache, ILogger<AvatarService> logger)
    {
        _cache = cache;
        _logger = logger;

        UserAvatar.Loader = async (coordinator, avatarUrl) => await LoadAsync(coordinator, avatarUrl);
    }

    public void Attach(ICoordinatorSession? session)
    {
        _session = session;
        if (session != null)
            _coordinator = session.GetCurrentUser().CoordinatorInstanceId;
    }

    public Task<Bitmap?> LoadAsync(string? coordinatorInstanceId, string? avatarUrl)
    {
        var coordinator = coordinatorInstanceId ?? _coordinator;
        if (coordinator == null || AvatarAddress.IdOf(avatarUrl) is not { } avatarId)
            return Task.FromResult<Bitmap?>(null);

        var key = (coordinator, avatarId);
        if (_bitmaps.TryGetValue(key, out var kept) && kept.TryGetTarget(out var bitmap))
            return Task.FromResult<Bitmap?>(bitmap);

        return AwaitAsync(key, _loads.GetOrAdd(key, _ => Task.Run(() => ReadAsync(coordinator, avatarId))));
    }

    private async Task<Bitmap?> AwaitAsync((string, string) key, Task<Bitmap?> load)
    {
        try
        {
            return await load;
        }
        finally
        {
            // Taken out by those who waited, not by the load itself: it may end before it is put in
            _loads.TryRemove(new(key, load));
        }
    }

    public Task<PreparedAvatar?> PrepareAsync(Func<Task<Stream>> openContent) =>
        Task.Run(async () =>
        {
            try
            {
                await using var content = await OpenSeekableAsync(openContent);
                using var decoded = SkiaImages.Decode(content, PickedMaxSide);
                if (decoded == null)
                    return null;

                using var square = SkiaImages.CropSquare(decoded.Bitmap, Side);
                using var encoded = square.Encode(SKEncodedImageFormat.Webp, Quality);
                return encoded == null ? null : new PreparedAvatar(ToBitmap(square), encoded.ToArray(), MimeType);
            }
            catch (Exception exception)
            {
                LogDecodeFailed(exception);
                return null;
            }
        });

    private async Task<Bitmap?> ReadAsync(string coordinator, string avatarId)
    {
        var key = (coordinator, avatarId);
        try
        {
            var path = _cache.FindAvatar(coordinator, avatarId) ?? await FetchAsync(coordinator, avatarId);
            if (path == null)
                return null;

            await using var content = File.OpenRead(path);
            using var decoded = SkiaImages.Decode(content, Side);
            if (decoded == null)
                return null;

            var bitmap = ToBitmap(decoded.Bitmap);
            _bitmaps[key] = new WeakReference<Bitmap>(bitmap);
            return bitmap;
        }
        catch (Exception exception)
        {
            LogDecodeFailed(exception);
            return null;
        }
    }

    private async Task<string?> FetchAsync(string coordinator, string avatarId)
    {
        // Only the coordinator we are connected to can be asked
        if (_session is not { } session || _coordinator != coordinator)
            return null;

        return await _cache.StoreAvatarAsync(coordinator, avatarId, async stream =>
            await session.DownloadAvatar(avatarId, stream) == MediaTransferResult.NoError);
    }

    private static Bitmap ToBitmap(SKBitmap bitmap) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Premul, bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);

    /// <summary>
    /// The decoder goes back and forth in the content; a source that cannot is read into memory first.
    /// </summary>
    private static async Task<Stream> OpenSeekableAsync(Func<Task<Stream>> openContent)
    {
        var content = await openContent();
        if (content.CanSeek)
            return content;

        await using (content)
        {
            var buffered = new MemoryStream();
            await content.CopyToAsync(buffered);
            buffered.Position = 0;
            return buffered;
        }
    }

    [LoggerMessage(LogLevel.Debug, "An avatar could not be decoded")]
    private partial void LogDecodeFailed(Exception exception);
}
