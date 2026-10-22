using System;
using Client.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Client.Avalonia.Styles.Scaling;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using Client.Application.Coordinator;
using Client.Application.Sounds;
using Client.Core.Push;
using Client.Views.Connections;
using Client.Views.Dialogs;
using Client.Views.Profile;
using Client.Views.ServerSettings;
using Client.Views.Sessions.Create;
using Client.Views.Sessions.Hybrid;
using Client.Views.Sessions.Hybrid.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Client.Views.Settings;
using Client.Views.Common;
using Client.Services.Platform;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Main;

internal partial class MainWindow : ViewWindow<MainWindowVM>
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
    public ISoundService? Sounds { get; init; }

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
                ViewModel!.ShowConnectWindow.RegisterHandler(async interaction =>
                {
                    interaction.SetOutput(await InWindowDialog.ShowAsync<ICoordinatorSession?>(
                        new ConnectView { DataContext = interaction.Input }, this, null));
                }).DisposeWith(disposables);
            
                ViewModel!.ShowSettings
                    .RegisterDialogHandler(this, () => new SettingsView(), fillWindow: true)
                    .DisposeWith(disposables);
                ViewModel!.ShowServerSettings
                    .RegisterDialogHandler(this, () => new ServerSettingsView(), fillWindow: true)
                    .DisposeWith(disposables);
                ViewModel!.ShowCreateSessionWindow
                    .RegisterDialogHandler(this, () => new CreateSessionView())
                    .DisposeWith(disposables);
                ViewModel!.ShowEditProfileWindow
                    .RegisterDialogHandler(this, () => new EditProfileView())
                    .DisposeWith(disposables);
                ViewModel!.ShowUserProfileWindow
                    .RegisterDialogHandler(this, () => new UserProfileView())
                    .DisposeWith(disposables);
                ViewModel!.ShowHybridSessionSettingsWindow
                    .RegisterDialogHandler(this, () => new HybridSessionSettingsView())
                    .DisposeWith(disposables);
                ViewModel!.ShowSessionPermissionsWindow
                    .RegisterDialogHandler(this, () => new Client.Views.Sessions.Permissions.SessionPermissionsView())
                    .DisposeWith(disposables);

                ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
                ViewModel!.AskDatabasePassword.RegisterDatabasePasswordHandler(this).DisposeWith(disposables);

                ObservableExtensions.Subscribe(ViewModel!.WatchProperty(nameof(MainWindowVM.IsContentFullScreen), vm => vm.IsContentFullScreen),
                    ApplyFullScreen).DisposeWith(disposables);

                // The hybrid session runs on while a chat is shown, with its controls in the side panel,
                // so its dialogs belong to the window and not to the view of the session
                var hybridDialogs = new System.Reactive.Disposables.SerialDisposable().DisposeWith(disposables);
                ObservableExtensions.Subscribe(ViewModel!.WatchProperty(nameof(MainWindowVM.HybridSession), vm => vm.HybridSession),
                    session => hybridDialogs.Disposable = session == null ? null : RegisterHybridDialogs(session))
                    .DisposeWith(disposables);

                var viewModel = ViewModel!;
                // The shown chat is read only while the window has the focus
                ObservableExtensions.Subscribe(this.GetObservable(IsActiveProperty),
                    isActive => viewModel.IsWindowActive = isActive).DisposeWith(disposables);

                viewModel.ChatMessageReceived += OnChatMessageReceived;
                System.Reactive.Disposables.Disposable.Create(() => viewModel.ChatMessageReceived -= OnChatMessageReceived)
                    .DisposeWith(disposables);

                ViewModel?.Initialize();
            });
        }
    }

    // The message is of somebody else and its chat is not muted. The sound is made also for the
    // chat the user is looking at
    private void OnChatMessageReceived(PushPayload message) =>
        Sounds?.Play(message.Mention ? SoundEvent.Mention : SoundEvent.Message);

    private MultipleDisposable RegisterHybridDialogs(ActiveHybridSessionVM session) => new()
    {
        session.ShowMessageBox.RegisterMessageBoxHandler(this),
        session.SelectSources.RegisterHandler(async interaction =>
        {
            interaction.SetOutput(await InWindowDialog.ShowAsync<ScreenShareRequest?>(
                new HybridSourceSelectView(interaction.Input), this, null));
        })
    };

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

        if (change.Property != WindowStateProperty || MainContentControl == null)
            return;

        var isFullScreen = WindowState == WindowState.FullScreen;

        if (isFullScreen)
            _stateBeforeFullScreen = change.GetOldValue<WindowState>();
        if (ViewModel != null)
            ViewModel.IsContentFullScreen = isFullScreen;

        Grid.SetColumn(MainContentControl, isFullScreen ? 0 : 2);
        Grid.SetColumnSpan(MainContentControl, isFullScreen ? 3 : 1);
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
        _ = VideoSurfaceProbe.RunAsync(this, App.Services?.GetService<ILogger<MainWindow>>());
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || e.Key != Key.Escape || WindowState != WindowState.FullScreen)
            return;

        ViewModel?.ExitFullScreen();
        e.Handled = true;
    }
}