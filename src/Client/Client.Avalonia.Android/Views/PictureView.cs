using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Client.Core.Media;
using Client.ViewModels.Sessions.Hybrid;
using SkiaSharp;

namespace Client.Droid.Views;

/// <summary>
/// Shows the pictures of one screen as libwebrtc decodes them on Android: I420 planes in memory,
/// which Skia uploads as they are and turns into RGB on the GPU with a shader. Only the latest
/// picture is kept; one that comes before the previous was drawn replaces it. Given no height, it
/// takes the one of the picture at its width.
/// </summary>
public sealed class PictureView : Control
{
    // What a screen is taken for before its first picture
    private const double DefaultAspect = 16.0 / 9.0;

    public static readonly StyledProperty<ScreenStreamVM?> StreamProperty =
        AvaloniaProperty.Register<PictureView, ScreenStreamVM?>(nameof(Stream));

    /// <summary>The screen whose pictures are shown, while the view is on screen.</summary>
    public ScreenStreamVM? Stream
    {
        get => GetValue(StreamProperty);
        set => SetValue(StreamProperty, value);
    }

    // The stream the pictures come from now
    private ScreenStreamVM? _attached;

    // Takes the planes, sampled in pixels of the luma one, and the conversion of the frame: what V
    // adds to red, U and V to green, and U to blue
    private const string YuvShader = """
        uniform shader luma;
        uniform shader chromaU;
        uniform shader chromaV;
        uniform float2 chromaScale;
        uniform float3 offset;
        uniform float2 range;
        uniform float4 matrix;

        half4 main(float2 position)
        {
            float2 chroma = position * chromaScale;
            float y = (luma.eval(position).r - offset.x) * range.x;
            float u = (chromaU.eval(chroma).r - offset.y) * range.y;
            float v = (chromaV.eval(chroma).r - offset.z) * range.y;
            return half4(saturate(y + matrix.x * v), saturate(y + matrix.y * u + matrix.z * v),
                saturate(y + matrix.w * u), 1.0);
        }
        """;

    private static SKRuntimeEffect? _effect;
    private static bool _isEffectBroken;

    // Set on the thread of the stream, taken on the UI thread
    private MemoryYuvVideoFrame? _pending;

    // Drawn now; touched on the UI thread only
    private MemoryYuvVideoFrame? _shown;
    private int _wakeQueued;

    /// <summary>The size of the last picture, or empty before one.</summary>
    public PixelSize PictureSize { get; private set; }

    /// <summary>
    /// Takes a picture on the thread of its stream. Only I420 in memory is drawn; the view keeps a
    /// reference of its own.
    /// </summary>
    public void PushFrame(RawVideoFrame frame)
    {
        if (frame.Gpu is not MemoryYuvVideoFrame { Layout: YuvPlaneLayout.I420 } yuv || yuv.Retain() == null)
            return;

        Interlocked.Exchange(ref _pending, yuv)?.Dispose();

        // One wake-up for any number of pictures that come before it runs
        if (Interlocked.Exchange(ref _wakeQueued, 1) == 0)
            Dispatcher.UIThread.Post(Wake, DispatcherPriority.Render);
    }

    /// <summary>Stops showing the last picture, once the stream it came from is over.</summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _pending, null)?.Dispose();
        Dispatcher.UIThread.Post(() =>
        {
            _shown?.Dispose();
            _shown = null;
            InvalidateVisual();
        });
    }

    private void Wake()
    {
        Interlocked.Exchange(ref _wakeQueued, 0);

        if (Interlocked.Exchange(ref _pending, null) is not { } next)
            return;

        _shown?.Dispose();
        _shown = next;

        if (PictureSize.Width != next.Width || PictureSize.Height != next.Height)
        {
            PictureSize = new PixelSize(next.Width, next.Height);
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Height) || double.IsInfinity(availableSize.Width))
            return base.MeasureOverride(availableSize);

        var aspect = PictureSize.Width > 0 && PictureSize.Height > 0
            ? (double)PictureSize.Width / PictureSize.Height
            : DefaultAspect;

        return new Size(availableSize.Width, availableSize.Width / aspect);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StreamProperty)
            Attach(TopLevel.GetTopLevel(this) != null ? Stream : null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attach(Stream);
    }

    // The stream outlives the view, and would keep it otherwise
    private void Attach(ScreenStreamVM? stream)
    {
        if (ReferenceEquals(_attached, stream))
            return;

        if (_attached != null)
        {
            _attached.FrameReady -= PushFrame;
            _attached.Cleared -= Clear;
        }

        _attached = stream;
        Clear();

        if (stream == null)
            return;

        stream.FrameReady += PushFrame;
        stream.Cleared += Clear;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_shown is not { } frame || frame.Retain() == null)
            return;

        var bounds = new Rect(Bounds.Size);
        var scale = Math.Min(bounds.Width / frame.Width, bounds.Height / frame.Height);
        var width = frame.Width * scale;
        var height = frame.Height * scale;

        // The drawing owns a reference until the renderer is done with it
        context.Custom(new PictureDrawing(bounds,
            new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height), frame));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        Attach(null);
        Interlocked.Exchange(ref _pending, null)?.Dispose();
        _shown?.Dispose();
        _shown = null;
    }

    private static SKRuntimeEffect? GetEffect()
    {
        if (_effect != null || _isEffectBroken)
            return _effect;

        _effect = SKRuntimeEffect.CreateShader(YuvShader, out var errors);
        _isEffectBroken = _effect == null;

        if (_isEffectBroken)
            Serilog.Log.ForContext<PictureView>().Error("The shader of the screens does not compile: {Errors}", errors);

        return _effect;
    }

    private sealed class PictureDrawing(Rect bounds, Rect place, MemoryYuvVideoFrame frame) : ICustomDrawOperation
    {
        private int _isDisposed;

        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        // On the render thread
        public void Render(ImmediateDrawingContext context)
        {
            if (Volatile.Read(ref _isDisposed) != 0 ||
                context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature ||
                GetEffect() is not { } effect)
                return;

            using var lease = feature.Lease();

            var chromaWidth = (frame.Width + 1) / 2;
            var chromaHeight = (frame.Height + 1) / 2;
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

            // Copied: Skia may upload them only when it flushes, after the picture went back
            using var luma = Plane(frame.Planes[0], frame.Width, frame.Height);
            using var chromaU = Plane(frame.Planes[1], chromaWidth, chromaHeight);
            using var chromaV = Plane(frame.Planes[2], chromaWidth, chromaHeight);

            if (luma == null || chromaU == null || chromaV == null)
                return;

            // Not disposed as a whole: that would dispose the effect too, which is kept for every picture
            var builder = new SKRuntimeShaderBuilder(effect);
            using var uniforms = builder.Uniforms;
            using var children = builder.Children;

            builder.Children["luma"] = luma.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            builder.Children["chromaU"] = chromaU.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            builder.Children["chromaV"] = chromaV.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling);
            builder.Uniforms["chromaScale"] = new[]
            {
                (float)chromaWidth / frame.Width, (float)chromaHeight / frame.Height
            };

            // As the OpenGL renderer of the desktop does it
            var (lumaFactor, chromaFactor) = frame.IsFullRange ? (1f, 1f) : (255f / 219f, 255f / 224f);
            builder.Uniforms["offset"] = new[] { frame.IsFullRange ? 0f : 16f / 255f, 128f / 255f, 128f / 255f };
            builder.Uniforms["range"] = new[] { lumaFactor, chromaFactor };
            builder.Uniforms["matrix"] = frame.Matrix == VideoColorMatrix.Bt601
                ? new[] { 1.402f, -0.344136f, -0.714136f, 1.772f }
                : new[] { 1.5748f, -0.1873f, -0.4681f, 1.8556f };

            // The shader works in pixels of the picture, laid over the place it goes to
            var matrix = SKMatrix.CreateScale((float)(place.Width / frame.Width), (float)(place.Height / frame.Height))
                .PostConcat(SKMatrix.CreateTranslation((float)place.X, (float)place.Y));

            using var built = builder.Build();
            using var shader = built.WithLocalMatrix(matrix);
            using var paint = new SKPaint { Shader = shader };

            lease.SkCanvas.DrawRect((float)place.X, (float)place.Y, (float)place.Width, (float)place.Height, paint);
        }

        private static SKImage? Plane(VideoPlane plane, int width, int height) =>
            SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque), plane.Data,
                plane.Stride);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
                frame.Dispose();
        }
    }
}
