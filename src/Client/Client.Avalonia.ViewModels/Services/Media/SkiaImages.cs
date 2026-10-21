using System;
using System.IO;
using SkiaSharp;

namespace Client.Services;

/// <summary>
/// Decoding of user images with SkiaSharp: reduced to the size they are shown at and turned the
/// way their metadata says, which the image control does not do on its own.
/// </summary>
internal static class SkiaImages
{
    // A format that cannot be decoded reduced is decoded whole; more than this is not attempted
    private const long MaxDecodedPixels = 64L * 1024 * 1024;

    /// <param name="Width">Of the whole image, as it is shown.</param>
    public sealed record Decoded(SKBitmap Bitmap, SKEncodedImageFormat Format, int Width, int Height) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }

    /// <param name="content">A seekable stream.</param>
    /// <param name="maxSide">The longer side of the result is at most this.</param>
    /// <returns>Null when the content is not an image, or one too large to decode.</returns>
    public static Decoded? Decode(Stream content, int maxSide)
    {
        using var codec = SKCodec.Create(content);
        if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            return null;

        var origin = codec.EncodedOrigin;
        var scale = Math.Min(1f, (float)maxSide / Math.Max(codec.Info.Width, codec.Info.Height));

        // What the format can decode to directly: at least the size asked for
        var size = codec.GetScaledDimensions(scale);
        if ((long)size.Width * size.Height > MaxDecodedPixels)
            return null;

        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                bitmap.Dispose();
                return null;
            }

            bitmap = Reduce(bitmap, maxSide);
            bitmap = Turn(bitmap, origin);

            var swapped = IsSideways(origin);
            return new Decoded(bitmap, codec.EncodedFormat,
                swapped ? codec.Info.Height : codec.Info.Width,
                swapped ? codec.Info.Width : codec.Info.Height);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The middle of the image as a square with a side of at most <paramref name="side"/>.
    /// </summary>
    public static SKBitmap CropSquare(SKBitmap bitmap, int side)
    {
        var shorter = Math.Min(bitmap.Width, bitmap.Height);
        var target = Math.Min(shorter, side);

        var square = new SKBitmap(bitmap.Info.WithSize(target, target));
        try
        {
            using var canvas = new SKCanvas(square);
            canvas.Clear(SKColors.Transparent);
            using var image = SKImage.FromBitmap(bitmap);
            var left = (bitmap.Width - shorter) / 2;
            var top = (bitmap.Height - shorter) / 2;
            canvas.DrawImage(image, new SKRect(left, top, left + shorter, top + shorter),
                new SKRect(0, 0, target, target), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return square;
        }
        catch
        {
            square.Dispose();
            throw;
        }
    }

    public static string? MimeTypeOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => "image/jpeg",
        SKEncodedImageFormat.Png => "image/png",
        SKEncodedImageFormat.Webp => "image/webp",
        SKEncodedImageFormat.Gif => "image/gif",
        _ => null
    };

    private static SKBitmap Reduce(SKBitmap bitmap, int maxSide)
    {
        var longer = Math.Max(bitmap.Width, bitmap.Height);
        if (longer <= maxSide)
            return bitmap;

        var factor = (double)maxSide / longer;
        var info = bitmap.Info.WithSize(Math.Max(1, (int)Math.Round(bitmap.Width * factor)),
            Math.Max(1, (int)Math.Round(bitmap.Height * factor)));

        using (bitmap)
        {
            return bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                   ?? throw new InvalidOperationException("The image could not be resized.");
        }
    }

    private static bool IsSideways(SKEncodedOrigin origin) => origin is SKEncodedOrigin.LeftTop
        or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// Puts the stored pixels where the origin says they are shown.
    /// </summary>
    private static SKBitmap Turn(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        float w = bitmap.Width, h = bitmap.Height;

        // Each maps a stored point (x, y) to its place in the shown image
        SKMatrix? matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null
        };

        if (matrix is not { } transform)
            return bitmap;

        using (bitmap)
        {
            var turned = new SKBitmap(IsSideways(origin)
                ? bitmap.Info.WithSize(bitmap.Height, bitmap.Width)
                : bitmap.Info);
            using var canvas = new SKCanvas(turned);
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(transform);
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
            return turned;
        }
    }
}
