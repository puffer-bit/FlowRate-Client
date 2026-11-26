using Client.Avalonia.Localization;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Client.Core.Settings;
using Client.Application.Settings;
using Microsoft.Extensions.Logging;
using CloseAppDialog = Client.Views.Dialogs.CloseAppDialog;
using Client.Views.Dialogs;
using Client.Views.Main;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;

namespace Client.Services.Tray;

/// <summary>
/// The tray icon. Depending on the Application behavior settings, the close button of the main window
/// hides it to the tray, and a click on the icon brings it back. Until a choice is remembered, closing asks.
/// Minimizing stays a regular minimize.
/// While we are in a hybrid session, the icon shows whether we speak, and whether the microphone or the
/// sound is off or we are away, and the menu turns them on and off. Outside of one, a dot on the icon tells
/// that some chat has unread messages.
/// </summary>
internal sealed partial class TrayService : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly Window _window;
    private readonly IBehaviorService _behaviorService;
    private readonly ILogger<TrayService> _logger;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _openItem;
    private readonly NativeMenuItem _exitItem;
    private readonly NativeMenuItem _microphoneItem;
    private readonly NativeMenuItem _soundItem;
    private readonly NativeMenuItem _afkItem;
    private readonly NativeMenuItemSeparator _voiceSeparator;
    private readonly MainWindowVM? _mainVM;
    private readonly IDisposable? _trayMenuSubscription;

    // Shown while we are in no hybrid session, with a dot while some chat has unread messages
    private readonly WindowIcon _appIcon;
    private readonly WindowIcon _unreadAppIcon;

    // The session the icon follows, null outside of one
    private ActiveHybridSessionVM? _session;

    // How long a restored state must hold before it counts as the user bringing the window back
    private static readonly TimeSpan RestoreSettleTime = TimeSpan.FromMilliseconds(300);

    // The state to restore: a window minimized from maximized comes back maximized
    private WindowState _restoreState = WindowState.Normal;

    // Set once the application really exits, so the window close is not turned into hiding
    private bool _isExiting;
    private bool _isAskingOnClose;

    public TrayService(IClassicDesktopStyleApplicationLifetime desktop, Window window, IBehaviorService behaviorService,
        ILogger<TrayService> logger)
    {
        _desktop = desktop;
        _window = window;
        _behaviorService = behaviorService;
        _logger = logger;

        using (var appIcon = new Bitmap(
                   AssetLoader.Open(new Uri("avares://Client.Avalonia.Assets/AppIcons/Headphones/png/tray-purple-48.png"))))
        {
            _appIcon = TrayIconRenderer.Copy(appIcon, withUnreadDot: false);
            _unreadAppIcon = TrayIconRenderer.Copy(appIcon, withUnreadDot: true);
        }

        // A click on the icon does not restore the window on every Linux tray, so the menu can too
        _openItem = new NativeMenuItem();
        _openItem.Click += (_, _) => ShowWindow();

        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => Exit();

        _microphoneItem = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
        _microphoneItem.Click += (_, _) => _session?.StatusBar.ToggleMicrophoneCommand.Execute().Subscribe();

        _soundItem = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
        _soundItem.Click += (_, _) => _session?.StatusBar.ToggleSoundCommand.Execute().Subscribe();

        _afkItem = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
        _afkItem.Click += (_, _) => _session?.StatusBar.ToggleAfkCommand.Execute().Subscribe();

        _voiceSeparator = new NativeMenuItemSeparator();

        _trayIcon = new TrayIcon
        {
            Icon = _appIcon,
            Menu = new NativeMenu
            {
                Items =
                {
                    _openItem, new NativeMenuItemSeparator(),
                    _microphoneItem, _soundItem, _afkItem, _voiceSeparator,
                    _exitItem,
                },
            },
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();

        // A click on the Dock icon brings back the window hidden to the menu bar
        if (OperatingSystem.IsMacOS() && global::Avalonia.Application.Current?.TryGetFeature(typeof(IActivatableLifetime))
                is IActivatableLifetime activatable)
            activatable.Activated += (_, args) =>
            {
                if (args.Kind == ActivationKind.Reopen)
                    ShowWindow();
            };

        if (OperatingSystem.IsWindows())
            _trayMenuSubscription = Window.WindowOpenedEvent.AddClassHandler<Window>(OnTrayMenuOpened);

        UpdateMenuTexts();
        Strings.LanguageChanged += UpdateMenuTexts;

        TrayIcon.SetIcons(global::Avalonia.Application.Current!, new TrayIcons { _trayIcon });

        _mainVM = _window.DataContext as MainWindowVM;
        if (_mainVM != null)
        {
            _mainVM.PropertyChanged += OnMainPropertyChanged;
            _mainVM.SessionsList.PropertyChanged += OnSessionsListPropertyChanged;
            FollowSession(_mainVM.HybridSession);
        }
        else
        {
            UpdateVoiceState();
        }

        _window.PropertyChanged += OnWindowPropertyChanged;
        _window.Closing += OnWindowClosing;
        if (_window is MainWindow mainWindow)
            mainWindow.CloseRequested += OnCloseRequested;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty)
            return;

        var state = (WindowState)e.NewValue!;
        if (state == WindowState.Minimized)
            return;

        if (_window.ShowInTaskbar)
        {
            _restoreState = state;
            return;
        }

        // Hiding to the tray makes the window manager echo the taskbar change before the minimize,
        // which shows up here as a brief Normal state. Only a state that holds is a real restore,
        // e.g. from Alt+Tab, and brings the window back to the taskbar
        DispatcherTimer.RunOnce(() =>
        {
            if (_window.WindowState == WindowState.Minimized)
                return;

            _restoreState = _window.WindowState;
            _window.ShowInTaskbar = true;
        }, RestoreSettleTime);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Only the close button of the window is intercepted, not Exit, code or the OS shutting down
        if (_isExiting || e.IsProgrammatic || e.CloseReason != WindowCloseReason.WindowClosing)
            return;

        e.Cancel = true;
        if (!_isAskingOnClose)
            _ = HandleCloseAsync();
    }

    // The close button the window draws itself: the same as the one of the system
    private void OnCloseRequested()
    {
        if (!_isAskingOnClose)
            _ = HandleCloseAsync();
    }

    private async Task HandleCloseAsync()
    {
        try
        {
            var settings = await _behaviorService.GetSettingsAsync();
            var action = settings.CloseButtonAction;

            if (action == CloseButtonAction.Ask)
            {
                CloseAppDialogResult? answer;
                _isAskingOnClose = true;
                try
                {
                    answer = await InWindowDialog.ShowAsync<CloseAppDialogResult?>(new CloseAppDialog(), _window, null);
                }
                finally
                {
                    _isAskingOnClose = false;
                }

                // Cancelled: the window stays
                if (answer == null)
                    return;

                action = answer.Action;

                // Without "Remember my choice" the question comes again next time
                if (answer.RememberChoice)
                {
                    settings.CloseButtonAction = action;
                    await _behaviorService.SaveSettingsAsync(settings);
                }
            }

            if (action == CloseButtonAction.MinimizeToTray)
                HideToTray();
            else
                Exit();
        }
        catch (Exception ex)
        {
            LogWindowCloseFailed(ex);
            Exit();
        }
    }

    public void HideToTray()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            _window.Hide();
        }
        else
        {
            // On X11/XWayland a window hidden with Hide() can come back transparent after Show(),
            // so it is minimized and only leaves the taskbar instead
            _window.ShowInTaskbar = false;
            _window.WindowState = WindowState.Minimized;
        }
    }

    public void ShowWindow()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            _window.Show();

        _window.ShowInTaskbar = true;
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = _restoreState;
        _window.Activate();
    }

    private void Exit()
    {
        _isExiting = true;
        _desktop.Shutdown();
    }

    private void UpdateMenuTexts()
    {
        _openItem.Header = Strings.Get("tray.open");
        _exitItem.Header = Strings.Get("tray.exit");
        _microphoneItem.Header = Strings.Get("tray.microphone");
        _soundItem.Header = Strings.Get("tray.sound");
        _afkItem.Header = Strings.Get("tray.afk");
        UpdateVoiceState();
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowVM.HybridSession))
            FollowSession(_mainVM!.HybridSession);
    }

    // The unread state comes from the thread of the change
    private void OnSessionsListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionsListVM.HasUnread))
            UpdateVoiceState();
    }

    private void FollowSession(ActiveHybridSessionVM? session)
    {
        if (_session != null)
        {
            _session.PropertyChanged -= OnSessionPropertyChanged;
            _session.StatusBar.PropertyChanged -= OnSessionPropertyChanged;
        }

        _session = session;
        if (_session != null)
        {
            _session.PropertyChanged += OnSessionPropertyChanged;
            _session.StatusBar.PropertyChanged += OnSessionPropertyChanged;
        }

        UpdateVoiceState();
    }

    // Some of the state comes straight from the coordinator, on its thread
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateVoiceState();

    private void UpdateVoiceState()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateVoiceState);
            return;
        }

        var state = GetVoiceState();

        var isInSession = state != null;
        _microphoneItem.IsVisible = isInSession && _session!.CanSpeak;
        _soundItem.IsVisible = isInSession;
        _afkItem.IsVisible = isInSession;
        _voiceSeparator.IsVisible = isInSession;

        if (state is not { } voiceState)
        {
            _trayIcon.Icon = _mainVM?.SessionsList.HasUnread == true ? _unreadAppIcon : _appIcon;
            return;
        }

        var statusBar = _session!.StatusBar;
        _microphoneItem.IsChecked = statusBar.IsMicrophoneOn;
        _soundItem.IsChecked = statusBar.IsSoundOn;
        _afkItem.IsChecked = statusBar.IsAfk;

        _trayIcon.Icon = TrayIconRenderer.Get(voiceState);
    }

    private TrayVoiceState? GetVoiceState()
    {
        if (_session is not { } session)
            return null;

        var statusBar = session.StatusBar;
        if (!statusBar.IsConnected)
            return TrayVoiceState.Connecting;
        if (statusBar.IsAfk)
            return TrayVoiceState.Afk;
        if (!statusBar.IsSoundOn)
            return TrayVoiceState.SoundOff;
        if (!session.CanSpeak)
            return TrayVoiceState.Idle;
        if (!statusBar.IsMicrophoneOn)
            return TrayVoiceState.MicrophoneOff;
        return statusBar.IsSpeaking ? TrayVoiceState.Speaking : TrayVoiceState.Idle;
    }


    public void Dispose()
    {
        _trayMenuSubscription?.Dispose();
        Strings.LanguageChanged -= UpdateMenuTexts;
        if (_mainVM != null)
        {
            _mainVM.PropertyChanged -= OnMainPropertyChanged;
            _mainVM.SessionsList.PropertyChanged -= OnSessionsListPropertyChanged;
        }
        FollowSession(null);
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Closing -= OnWindowClosing;
        if (_window is MainWindow mainWindow)
            mainWindow.CloseRequested -= OnCloseRequested;
        _trayIcon.Dispose();
    }
}
