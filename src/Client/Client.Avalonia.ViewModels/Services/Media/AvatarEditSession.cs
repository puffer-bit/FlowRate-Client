using System;
using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace Client.Services;

/// <summary>A decoded photo owned by the editor until it is accepted or dismissed.</summary>
public sealed class AvatarEditSession : IDisposable
{
    private readonly SKBitmap _original;
    private SKBitmap _bitmap;
    public IImage Picture { get; private set; }

    internal AvatarEditSession(SKBitmap bitmap)
    {
        _original = bitmap;
        _bitmap = bitmap;
        try
        {
            Picture = Pictures.ToBitmap(bitmap);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public void Transform(bool mirror)
    {
        var next = new SKBitmap(mirror ? _bitmap.Info : _bitmap.Info.WithSize(_bitmap.Height, _bitmap.Width));
        using (var canvas = new SKCanvas(next))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(mirror
                ? new SKMatrix(-1, 0, _bitmap.Width, 0, 1, 0, 0, 0, 1)
                : new SKMatrix(0, -1, _bitmap.Height, 1, 0, 0, 0, 0, 1));
            using var image = SKImage.FromBitmap(_bitmap);
            canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        }
        var picture = Pictures.ToBitmap(next);
        (Picture as IDisposable)?.Dispose();
        if (!ReferenceEquals(_bitmap, _original))
            _bitmap.Dispose();
        _bitmap = next;
        Picture = picture;
    }

    public void Reset()
    {
        var picture = Pictures.ToBitmap(_original);
        (Picture as IDisposable)?.Dispose();
        if (!ReferenceEquals(_bitmap, _original))
            _bitmap.Dispose();
        _bitmap = _original;
        Picture = picture;
    }

    /// <param name="crop">A square in normalized image coordinates.</param>
    public PreparedAvatar Prepare(Rect crop)
    {
        var side = (float)Math.Clamp(crop.Width * _bitmap.Width, 1, Math.Min(_bitmap.Width, _bitmap.Height));
        var left = (float)Math.Clamp(crop.X * _bitmap.Width, 0, _bitmap.Width - side);
        var top = (float)Math.Clamp(crop.Y * _bitmap.Height, 0, _bitmap.Height - side);
        using var square = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(square))
        using (var image = SKImage.FromBitmap(_bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(image, new SKRect(left, top, left + side, top + side),
                new SKRect(0, 0, 256, 256), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        using var encoded = square.Encode(SKEncodedImageFormat.Webp, 85);
        return new PreparedAvatar(Pictures.ToBitmap(square), encoded.ToArray(), "image/webp");
    }

    public void Dispose()
    {
        (Picture as IDisposable)?.Dispose();
        if (!ReferenceEquals(_bitmap, _original))
            _bitmap.Dispose();
        _original.Dispose();
    }
}
