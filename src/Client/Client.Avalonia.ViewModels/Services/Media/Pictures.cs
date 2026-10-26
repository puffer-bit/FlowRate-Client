using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Client.Avalonia.Controls.Images;
using SkiaSharp;

namespace Client.Services;

/// <summary>
/// User images turned into what the interface shows: an animated one moves, any other is still.
/// </summary>
internal static class Pictures
{
    /// <param name="content">A seekable stream.</param>
    /// <param name="maxSide">The longer side of the result is at most this.</param>
    /// <returns>Null when the content is not an image, or one too large to decode.</returns>
    public static IImage? Decode(Stream content, int maxSide)
    {
        using var codec = SKCodec.Create(content);
        if (codec == null)
            return null;

        using (var animation = SkiaImages.DecodeAnimation(codec, maxSide))
            if (animation != null)
                return ToAnimatedImage(animation);

        using var decoded = SkiaImages.Decode(codec, maxSide);
        return decoded == null ? null : ToBitmap(decoded.Bitmap);
    }

    /// <summary>A copy of the pixels, which the caller may dispose after.</summary>
    public static Bitmap ToBitmap(SKBitmap bitmap) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Premul, bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), bitmap.RowBytes);

    public static AnimatedImage ToAnimatedImage(SkiaImages.Animation animation)
    {
        var frames = new (Bitmap, TimeSpan)[animation.Frames.Length];
        for (var i = 0; i < frames.Length; i++)
            frames[i] = (ToBitmap(animation.Frames[i]), animation.Delays[i]);

        // An Avalonia object belongs to the thread it is made on, and a view reading it from another throws:
        // the frames are decoded anywhere, the image that shows them is made on the UI thread
        return Dispatcher.UIThread.Invoke(() => new AnimatedImage(frames));
    }
}
