using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;

namespace Client.Services;

/// <summary>
/// The images of a chat as the interface needs them: bitmaps of the reduced copies and of the
/// originals, fetched from the server once and kept in the media cache.
/// </summary>
public interface IChatImageService : IImagePreviewer
{
    /// <summary>The reduced copy of an image of a message, or null when it cannot be had now.</summary>
    Task<Bitmap?> LoadPreviewAsync(ICoordinatorSession session, int imageId);

    /// <summary>
    /// An animated image of a message as it moves, reduced as its copy is; the reduced copy holds
    /// only the first frame. Null when it cannot be had now or is not animated.
    /// </summary>
    Task<IImage?> LoadAnimationAsync(ICoordinatorSession session, int imageId);

    /// <summary>The file of the image itself, downloaded when it is not kept yet.</summary>
    /// <param name="progress">Reports how many bytes were received.</param>
    Task<(MediaTransferResult Result, string? Path)> FetchOriginalAsync(ICoordinatorSession session, int imageId,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// A picture of an image file, not larger than <paramref name="maxSide"/> on its longer side.
    /// An animated one moves.
    /// </summary>
    Task<IImage?> DecodeAsync(Func<Task<Stream>> openContent, int maxSide);
}
