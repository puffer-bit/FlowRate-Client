using System;
using System.Threading.Tasks;
using Client.Services;
using Avalonia;
using Avalonia.Controls;
using Client.Avalonia.Styles.Scaling;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using Client.Application.Sounds;
using Client.Avalonia.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Client.Views.Common;
using Client.Services.Platform;
using Client.ViewModels.Main;

namespace Client.Views.Main;

internal partial class MainWindow : ViewWindow<MainWindowVM>, IMainWindow
{
    // Corners of the window while it draws its own frame, as those of the dialogs
    private static readonly CornerRadius FrameCornerRadius = new(8);

    // The least size of the window at the usual scale, as the markup sets it
    private readonly Size _minSize;

    // What the window was before it went fullscreen, to bring a maximized one back maximized
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    /// <summary>
    /// Plays the sound of a message that came to a chat. Without it the messages come silently.
    /// </summary>
    public ISoundService? Sounds
    {
        get => MainViewControl.Sounds;
        init => MainViewControl.Sounds = value;
    }

    public MainWindow()
    {
        InitializeComponent();
        _minSize = new Size(MinWidth, MinHeight);
        WindowScale.Changed += FitMinSizeToScale;
        WindowChrome.Changed += ApplyChrome;
        Closed += (_, _) =>
        {
            WindowScale.Changed -= FitMinSizeToScale;
            WindowChrome.Changed -= ApplyChrome;
        };
        ApplyChrome();
        FitMinSizeToScale();
        if (!Design.IsDesignMode)
        {
            this.WhenActivated(disposables =>
            {
                // Fullscreen content and a fullscreen window are the same thing on the desktop
                ObservableExtensions.Subscribe(ViewModel!.WatchProperty(nameof(MainWindowVM.IsContentFullScreen), vm => vm.IsContentFullScreen),
                    ApplyFullScreen).DisposeWith(disposables);

                var viewModel = ViewModel!;
                // The shown chat is read only while the window has the focus
                ObservableExtensions.Subscribe(this.GetObservable(IsActiveProperty),
                    isActive => viewModel.IsWindowActive = isActive).DisposeWith(disposables);
            });
        }
    }

    /// <summary>
    /// Loads the application under its startup screen. <paramref name="showWindow"/> shows the window,
    /// for prompts that come before the application is loaded.
    /// </summary>
    public Task InitializeBeforeShowAsync(Action showWindow) => MainViewControl.InitializeBeforeShowAsync(showWindow);

    // Either the window draws its title bar itself, in the top bar, or the system does
    private void ApplyChrome()
    {
        ExtendClientAreaToDecorationsHint = WindowChrome.IsCustom;
        WindowDecorations = WindowChrome.IsCustom ? WindowDecorations.BorderOnly : WindowDecorations.Full;
    }

    /// <summary>
    /// Raised when the close button of the top bar is pressed. Closing from code is not a request
    /// of the user, so the button does not call <see cref="Window.Close()"/> itself.
    /// </summary>
    public event Action? CloseRequested;

    public void RequestClose()
    {
        if (CloseRequested is { } handler)
            handler();
        else
            Close();
    }

    // The content needs the same room at any scale, so the least size of the window grows and
    // shrinks with it, yet never past the screen
    private void FitMinSizeToScale()
    {
        var size = _minSize * WindowScale.Factor;

        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            var area = screen.WorkingArea.ToRect(screen.Scaling).Size;
            size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
        }

        MinWidth = size.Width;
        MinHeight = size.Height;

        if (WindowState != WindowState.Normal)
            return;

        if (Width < size.Width)
            Width = size.Width;
        if (Height < size.Height)
            Height = size.Height;
    }

    // Follows the window itself and not a command, so that fullscreen left by means of the system
    // gives the side panel its place back too
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The margin around a window that draws its own frame is its shadow. There is none while
        // the window is maximized or the system draws the frame: no corners to round, no line to draw
        if (change.Property == WindowDecorationMarginProperty)
        {
            var hasFrame = WindowDecorationMargin != default;

            X11FrameExtents.Set(this, WindowDecorationMargin);
            CornerRadius = hasFrame ? FrameCornerRadius : default;
            BorderThickness = new Thickness(hasFrame ? 1 : 0);
        }

        if (change.Property != WindowStateProperty)
            return;

        var isFullScreen = WindowState == WindowState.FullScreen;

        if (isFullScreen)
            _stateBeforeFullScreen = change.GetOldValue<WindowState>();
        if (ViewModel != null)
            ViewModel.IsContentFullScreen = isFullScreen;
    }

    private void ApplyFullScreen(bool isFullScreen)
    {
        if (isFullScreen)
            WindowState = WindowState.FullScreen;
        else if (WindowState == WindowState.FullScreen)
            WindowState = _stateBeforeFullScreen;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // The margin is known before the window of the system exists
        X11FrameExtents.Set(this, WindowDecorationMargin);

        // Before a session is joined: how its screens are drawn
        if (Client.Core.Media.MediaRuntime.IsEnabled)
            _ = VideoSurfaceProbe.RunAsync(this, App.Services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(VideoSurfaceProbe)));
    }
}
