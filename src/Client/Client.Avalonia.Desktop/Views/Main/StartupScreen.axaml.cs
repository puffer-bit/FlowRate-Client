using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Client.Avalonia.Styles.Animation;
using Client.Avalonia.Styles.Scaling;

namespace Client.Views.Main;

/// <summary>
/// What the main window shows over everything while the application starts: the theme-aware logo,
/// a ring and the current startup stage. Fades out once the application is loaded.
/// </summary>
internal partial class StartupScreen : UserControl
{
    public StartupScreen()
    {
        InitializeComponent();
    }

    public async Task FadeOutAsync(Control mainInterface)
    {
        var duration = Motion.Get(MotionDuration.Startup);
        if (duration <= TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var scaling = topLevel.RenderScaling * WindowScale.Factor;
        var pixelSize = PixelSize.FromSize(Bounds.Size, scaling);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            return;

        using var startup = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
        using var main = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
        startup.Render(this);
        main.Render(mainInterface);

        var originalContent = Content;
        var frame = new DissolveFrame(main, startup);
        Content = frame;
        try
        {
            await new global::Avalonia.Animation.Animation
            {
                Duration = duration,
                Easing = Motion.Ease(MotionEase.Standard),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(DissolveFrame.ProgressProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(DissolveFrame.ProgressProperty, 1d) } }
                }
            }.RunAsync(frame);
        }
        finally
        {
            // Hide before releasing the snapshots; the view model also records this state.
            SetCurrentValue(IsVisibleProperty, false);
            Content = originalContent;
        }
    }

    // Both images are drawn into one opaque visual. No intermediate frame depends on
    // re-compositing a translucent fullscreen control over the live main interface.
    private sealed class DissolveFrame(RenderTargetBitmap main, RenderTargetBitmap startup) : Control
    {
        public static readonly StyledProperty<double> ProgressProperty =
            AvaloniaProperty.Register<DissolveFrame, double>("Progress");

        static DissolveFrame() => AffectsRender<DissolveFrame>(ProgressProperty);

        public override void Render(DrawingContext context)
        {
            var destination = new Rect(Bounds.Size);
            context.DrawImage(main, destination);
            using (context.PushOpacity(1 - Math.Clamp(GetValue(ProgressProperty), 0, 1)))
                context.DrawImage(startup, destination);
        }
    }

    private void OnContinueClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Client.ViewModels.Main.MainWindowVM vm)
            vm.ContinueStartup();
    }

    private void OnExitClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // The screen covers the title bar the window may draw itself, so the window is moved by the screen
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window)
            window.BeginMoveDrag(e);
    }
}
