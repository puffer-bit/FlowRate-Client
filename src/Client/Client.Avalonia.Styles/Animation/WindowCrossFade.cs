using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// Blends every open window from the look it has into the look a change gives it. A change of the theme
/// replaces brushes all over the window at once, and a transition on each of them would let backgrounds,
/// text and icons drift apart; instead a picture of the window as it was is laid over it and fades out.
/// </summary>
public static class WindowCrossFade
{
    // Windows that still wear a picture: a change that comes meanwhile shows through it as it fades
    private static readonly HashSet<Window> Covered = [];

    /// <summary>
    /// Runs <paramref name="change"/> on the UI thread; with animations switched off it is all that happens.
    /// </summary>
    public static void Run(Action change)
    {
        var duration = Motion.Get(MotionDuration.Long);
        var covers = duration > TimeSpan.Zero ? CoverWindows() : [];

        change();

        if (covers.Count == 0)
            return;

        // The change is laid out and drawn first, so the fade does not spend its time behind that work
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var cover in covers)
                cover.Picture.Opacity = 0;

            DispatcherTimer.RunOnce(() =>
            {
                foreach (var cover in covers)
                    Remove(cover);
            }, duration);
        }, DispatcherPriority.Background);
    }

    private static List<Cover> CoverWindows()
    {
        var covers = new List<Cover>();

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return covers;

        var duration = Motion.Get(MotionDuration.Long);

        foreach (var window in desktop.Windows)
        {
            if (!window.IsVisible || window.WindowState == WindowState.Minimized || Covered.Contains(window))
                continue;

            var size = window.ClientSize;
            var scaling = window.RenderScaling;
            var pixelSize = PixelSize.FromSize(size, scaling);
            var layer = OverlayLayer.GetOverlayLayer(window);

            if (layer == null || pixelSize.Width <= 0 || pixelSize.Height <= 0)
                continue;

            var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(window);

            // A window that draws its own frame keeps a margin for the shadow around its content, and the
            // layer covers the content only: the picture is cut to it, or it would be squeezed into the
            // layer and show the change around itself
            var area = new PixelRect(pixelSize);
            if (layer.TransformToVisual(window) is { } toWindow)
            {
                var layerArea = PixelRect.FromRect(new Rect(layer.Bounds.Size).TransformToAABB(toWindow), scaling);
                layerArea = layerArea.Intersect(area);
                if (layerArea.Width > 0 && layerArea.Height > 0)
                    area = layerArea;
            }

            var picture = new Image
            {
                Source = area == new PixelRect(pixelSize) ? bitmap : new CroppedBitmap(bitmap, area),
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                Transitions =
                [
                    new DoubleTransition
                    {
                        Property = Visual.OpacityProperty,
                        Duration = duration,
                        Easing = Motion.Ease(MotionEase.Standard)
                    }
                ]
            };

            // The overlay layer does not stretch its children, so the picture follows its size. The layer
            // is measured in the units of the interface scale, which the change may be about
            var sizing = layer.GetObservable(Visual.BoundsProperty).Subscribe(new BoundsObserver(picture));

            layer.Children.Add(picture);
            Covered.Add(window);
            covers.Add(new Cover(window, layer, picture, bitmap, sizing));
        }

        return covers;
    }

    private static void Remove(Cover cover)
    {
        cover.Sizing.Dispose();
        cover.Layer.Children.Remove(cover.Picture);
        cover.Picture.Source = null;
        cover.Bitmap.Dispose();
        Covered.Remove(cover.Window);
    }

    private sealed record Cover(Window Window, OverlayLayer Layer, Image Picture, RenderTargetBitmap Bitmap,
        IDisposable Sizing);

    private sealed class BoundsObserver(Image picture) : IObserver<Rect>
    {
        public void OnNext(Rect bounds)
        {
            picture.Width = bounds.Width;
            picture.Height = bounds.Height;
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
