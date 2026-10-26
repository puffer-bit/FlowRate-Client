using System;
using System.Collections.Generic;
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

    // All the frames of one animation together, 64 MB; a longer one is reduced further to fit
    private const double MaxAnimationPixels = 16.0 * 1024 * 1024;
    private const int MaxFrames = 2000;

    // An animation that would have to be reduced below this is shown still instead
    private const int MinAnimationSide = 48;

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
        return codec == null ? null : Decode(codec, maxSide);
    }

    /// <inheritdoc cref="Decode(Stream, int)"/>
    public static Decoded? Decode(SKCodec codec, int maxSide)
    {
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0)
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

    /// <param name="Width">Of the whole image, as it is shown.</param>
    public sealed record Animation(SKBitmap[] Frames, TimeSpan[] Delays, int Width, int Height) : IDisposable
    {
        public void Dispose()
        {
            foreach (var frame in Frames)
                frame.Dispose();
        }
    }

    /// <summary>
    /// Every frame of an animated image, each one whole and reduced as <see cref="Decode"/> would.
    /// The frames together are kept within a memory budget, so a long animation is reduced further.
    /// </summary>
    /// <returns>Null when the image is not animated, or too large to decode.</returns>
    public static Animation? DecodeAnimation(SKCodec codec, int maxSide)
    {
        if (codec.FrameCount < 2 || codec.FrameCount > MaxFrames)
            return null;

        int width = codec.Info.Width, height = codec.Info.Height;
        if (width <= 0 || height <= 0 || (long)width * height > MaxDecodedPixels)
            return null;

        var scale = Math.Min(1.0, (double)maxSide / Math.Max(width, height));
        var reducedPixels = width * scale * height * scale * codec.FrameCount;
        if (reducedPixels > MaxAnimationPixels)
            scale *= Math.Sqrt(MaxAnimationPixels / reducedPixels);

        var target = new SKImageInfo(Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)), SKColorType.Bgra8888, SKAlphaType.Premul);
        if (Math.Max(target.Width, target.Height) < MinAnimationSide)
            return null;

        var infos = codec.FrameInfo;
        var whole = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // A frame is drawn over the one it requires; one that is not just before it is kept for that
        var keep = new HashSet<int>();
        for (var i = 0; i < infos.Length; i++)
            if (infos[i].RequiredFrame >= 0 && infos[i].RequiredFrame != i - 1)
                keep.Add(infos[i].RequiredFrame);

        var kept = new Dictionary<int, SKBitmap>();
        var frames = new List<SKBitmap>(infos.Length);
        var delays = new List<TimeSpan>(infos.Length);
        using var canvas = new SKBitmap(whole);
        try
        {
            for (var i = 0; i < infos.Length; i++)
            {
                var required = infos[i].RequiredFrame;
                if (required < 0)
                    canvas.Erase(SKColors.Transparent);
                else if (required != i - 1)
                    kept[required].GetPixelSpan().CopyTo(canvas.GetPixelSpan());

                var result = codec.GetPixels(whole, canvas.GetPixels(), new SKCodecOptions(i, required));
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                    break;

                if (keep.Contains(i))
                    kept[i] = canvas.Copy();

                frames.Add(target.Width == width && target.Height == height
                    ? canvas.Copy()
                    : canvas.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                      ?? throw new InvalidOperationException("The image could not be resized."));
                delays.Add(TimeSpan.FromMilliseconds(infos[i].Duration));
            }
        }
        catch
        {
            foreach (var frame in frames)
                frame.Dispose();
            throw;
        }
        finally
        {
            foreach (var bitmap in kept.Values)
                bitmap.Dispose();
        }

        if (frames.Count >= 2)
            return new Animation(frames.ToArray(), delays.ToArray(), width, height);

        foreach (var frame in frames)
            frame.Dispose();
        return null;
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
