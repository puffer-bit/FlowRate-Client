using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Client.Avalonia.Controls.Avatars;
using Client.Avalonia.Controls.Images;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.Core.Users;
using Microsoft.Extensions.Logging;
using Shared.Validation;
using SkiaSharp;

namespace Client.Services;

/// <summary>
/// A picture picked for an avatar, made as small as an avatar is shown.
/// </summary>
/// <param name="Picture">What the dialog shows until the picture is saved; owned by the receiver.</param>
/// <param name="Encoded">What goes to the server.</param>
public sealed record PreparedAvatar(IImage Picture, byte[] Encoded, string MimeType);

/// <summary>Why a picked file cannot be an avatar.</summary>
public enum AvatarRejection
{
    None,
    NotAnImage,

    /// <summary>
    /// An animation goes to the server as it is, since it cannot be encoded again, and the server
    /// takes no more than <see cref="Limits.AvatarMaxSize"/>.
    /// </summary>
    AnimationTooLarge
}

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
    Task<IImage?> LoadAsync(string? coordinatorInstanceId, string? avatarUrl);

    /// <summary>
    /// Cuts the middle square out of a picked image and reduces it. An animated one is kept as it
    /// is, so it still moves; the avatar shows its middle all the same.
    /// </summary>
    Task<(PreparedAvatar? Avatar, AvatarRejection Rejection)> PrepareAsync(Func<Task<Stream>> openContent);
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
    private readonly ConcurrentDictionary<(string Coordinator, string AvatarId), WeakReference<IImage>> _pictures = new();

    // One download and one decoding per avatar at a time, however many views ask for it
    private readonly ConcurrentDictionary<(string Coordinator, string AvatarId), Task<IImage?>> _loads = new();

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

    public Task<IImage?> LoadAsync(string? coordinatorInstanceId, string? avatarUrl)
    {
        var coordinator = coordinatorInstanceId ?? _coordinator;
        if (coordinator == null || AvatarAddress.IdOf(avatarUrl) is not { } avatarId)
            return Task.FromResult<IImage?>(null);

        var key = (coordinator, avatarId);
        if (_pictures.TryGetValue(key, out var kept) && kept.TryGetTarget(out var picture))
            return Task.FromResult<IImage?>(picture);

        return AwaitAsync(key, _loads.GetOrAdd(key, _ => Task.Run(() => ReadAsync(coordinator, avatarId))));
    }

    private async Task<IImage?> AwaitAsync((string, string) key, Task<IImage?> load)
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

    public Task<(PreparedAvatar? Avatar, AvatarRejection Rejection)> PrepareAsync(Func<Task<Stream>> openContent) =>
        Task.Run(async () =>
        {
            try
            {
                var bytes = await ReadAllAsync(openContent);
                if (PrepareAnimated(bytes) is { } animated)
                    return animated;

                using var content = new MemoryStream(bytes, writable: false);
                using var decoded = SkiaImages.Decode(content, PickedMaxSide);
                if (decoded == null)
                    return (null, AvatarRejection.NotAnImage);

                using var square = SkiaImages.CropSquare(decoded.Bitmap, Side);
                using var encoded = square.Encode(SKEncodedImageFormat.Webp, Quality);
                return encoded == null
                    ? (null, AvatarRejection.NotAnImage)
                    : (new PreparedAvatar(Pictures.ToBitmap(square), encoded.ToArray(), MimeType), AvatarRejection.None);
            }
            catch (Exception exception)
            {
                LogDecodeFailed(exception);
                return ((PreparedAvatar?)null, AvatarRejection.NotAnImage);
            }
        });

    /// <summary>
    /// Null when the picked file is not an animation, which is then made still like any other image.
    /// </summary>
    private static (PreparedAvatar?, AvatarRejection)? PrepareAnimated(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes, writable: false));
        if (codec is not { FrameCount: > 1 } || SkiaImages.MimeTypeOf(codec.EncodedFormat) is not
                { } mimeType || !Limits.ChatImageMimeTypes.Contains(mimeType))
            return null;

        if (bytes.Length > Limits.AvatarMaxSize)
            return (null, AvatarRejection.AnimationTooLarge);

        // Shown as a whole, not cut square: the avatar fills its circle with the middle of it anyway
        using var animation = SkiaImages.DecodeAnimation(codec, Side);
        return animation == null
            ? null
            : (new PreparedAvatar(Pictures.ToAnimatedImage(animation), bytes, mimeType), AvatarRejection.None);
    }

    private async Task<IImage?> ReadAsync(string coordinator, string avatarId)
    {
        var key = (coordinator, avatarId);
        try
        {
            var path = _cache.FindAvatar(coordinator, avatarId) ?? await FetchAsync(coordinator, avatarId);
            if (path == null)
                return null;

            await using var content = File.OpenRead(path);
            if (Pictures.Decode(content, Side) is not { } picture)
                return null;

            _pictures[key] = new WeakReference<IImage>(picture);
            return picture;
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

    /// <summary>
    /// The picked file in memory: an animation is uploaded byte for byte, and the decoder goes back
    /// and forth in it. Its size was checked before.
    /// </summary>
    private static async Task<byte[]> ReadAllAsync(Func<Task<Stream>> openContent)
    {
        await using var content = await openContent();
        using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered);
        return buffered.ToArray();
    }

    [LoggerMessage(LogLevel.Debug, "An avatar could not be decoded")]
    private partial void LogDecodeFailed(Exception exception);
}
