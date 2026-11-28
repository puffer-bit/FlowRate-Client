using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Chat;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Client.Services;

/// <summary>
/// Compresses images and takes their metadata out before they are sent, with SkiaSharp. An opaque image
/// becomes a JPEG, one with transparency a WebP; an animated one is never compressed, as only its first
/// frame would be left.
/// </summary>
public sealed partial class SkiaImagePreparer(ILogger<SkiaImagePreparer>? logger = null) : IImagePreparer
{
    // A larger file is sent as it is: it would take too much memory, and is not a photo anyway
    private const long MaxPreparedSize = 256L * 1024 * 1024;

    public Task<PreparedImage?> PrepareAsync(ChatMediaSource source, ImagePreparation preparation,
        CancellationToken cancellationToken = default)
    {
        if (source.Size > MaxPreparedSize)
            return Task.FromResult<PreparedImage?>(null);

        return ImageDecoding.RunAsync(async () =>
        {
            byte[] original;
            await using (var content = await source.Open())
            {
                using var buffer = new MemoryStream(source.Size > 0 ? (int)source.Size : 0);
                await content.CopyToAsync(buffer, cancellationToken);
                original = buffer.ToArray();
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return Prepare(source.FileName, original, preparation);
            }
            catch (Exception exception)
            {
                if (logger != null)
                    LogPrepareFailed(logger, exception, source.FileName);
                return null;
            }
        }, cancellationToken);
    }

    private static PreparedImage? Prepare(string fileName, byte[] original, ImagePreparation preparation)
    {
        using var data = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(data);
        if (codec == null || SkiaImages.MimeTypeOf(codec.EncodedFormat) == null)
            return null;

        var sideways = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = sideways ? codec.Info.Height : codec.Info.Width;
        var height = sideways ? codec.Info.Width : codec.Info.Height;
        var canCompress = codec.FrameCount < 2;

        if (preparation.Compress && canCompress && Compress(fileName, original, codec, preparation) is { } compressed)
            return new PreparedImage(compressed.Content, compressed.FileName, width, height, true);

        var stripped = preparation.StripMetadata ? ImageMetadata.Strip(original) : null;
        return new PreparedImage(stripped == null || ReferenceEquals(stripped, original) ? null : stripped,
            fileName, width, height, canCompress);
    }

    /// <returns>Null when the original is better sent as it is: as small already, and not too large.</returns>
    private static (byte[] Content, string FileName)? Compress(string fileName, byte[] original, SKCodec codec,
        ImagePreparation preparation)
    {
        var policy = preparation.Policy;
        var reduced = Math.Max(codec.Info.Width, codec.Info.Height) > policy.MaxSide;
        var isJpeg = codec.EncodedFormat == SKEncodedImageFormat.Jpeg;

        using var decoded = SkiaImages.Decode(codec, policy.MaxSide);
        if (decoded == null)
            return null;

        var opaque = codec.Info.AlphaType == SKAlphaType.Opaque || IsOpaque(decoded.Bitmap);
        using var encoded = decoded.Bitmap.Encode(opaque ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Webp,
            policy.Quality);
        if (encoded == null)
            return null;

        var content = encoded.ToArray();

        // The color profile always goes along, or the colors of a wide-gamut photo would change; the EXIF
        // only when it is kept, set upright as the pixels are turned now
        if (opaque && isJpeg)
            content = ImageMetadata.InsertJpegSegments(content,
                ImageMetadata.CopyableJpegSegments(original, withExif: !preparation.StripMetadata));

        // A turned photo is turned in its pixels now, which saves the viewer from turning it
        if (!reduced && codec.EncodedOrigin == SKEncodedOrigin.TopLeft && content.Length >= original.Length)
            return null;

        var extension = Path.GetExtension(fileName);
        var name = opaque
            ? extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : Path.ChangeExtension(fileName, ".jpg")
            : Path.ChangeExtension(fileName, ".webp");
        return (content, name);
    }

    private static bool IsOpaque(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888 && bitmap.ColorType != SKColorType.Rgba8888)
            return false;

        var pixels = bitmap.GetPixelSpan();
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0xFF)
                return false;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not prepare the image {FileName} for sending.")]
    private static partial void LogPrepareFailed(ILogger logger, Exception exception, string fileName);
}
