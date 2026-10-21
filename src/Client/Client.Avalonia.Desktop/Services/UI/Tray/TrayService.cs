using Client.Avalonia.Localization;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Client.Core.Settings;
using Client.Application.Settings;
using Microsoft.Extensions.Logging;
using CloseAppDialog = Client.Views.Dialogs.CloseAppDialog;
using Client.Views.Dialogs;
using Client.Views.Main;

namespace Client.Services.Tray;

/// <summary>
/// The tray icon. Depending on the Application behavior settings, the close button of the main window
/// hides it to the tray, and a click on the icon brings it back. Until a choice is remembered, closing asks.
/// Minimizing stays a regular minimize.
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

        // A click on the icon does not restore the window on every Linux tray, so the menu can too
        _openItem = new NativeMenuItem();
        _openItem.Click += (_, _) => ShowWindow();

        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => Exit();

        UpdateMenuTexts();
        Strings.LanguageChanged += UpdateMenuTexts;

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Client.Avalonia.Desktop/Assets/avalonia-logo.ico"))),
            ToolTipText = "FlowRate",
            Menu = new NativeMenu
            {
                Items = { _openItem, new NativeMenuItemSeparator(), _exitItem },
            },
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();

        TrayIcon.SetIcons(global::Avalonia.Application.Current!, new TrayIcons { _trayIcon });

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

    private void HideToTray()
    {
        if (OperatingSystem.IsWindows())
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

    private void ShowWindow()
    {
        if (OperatingSystem.IsWindows())
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
    }

    public void Dispose()
    {
        Strings.LanguageChanged -= UpdateMenuTexts;
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Closing -= OnWindowClosing;
        if (_window is MainWindow mainWindow)
            mainWindow.CloseRequested -= OnCloseRequested;
        _trayIcon.Dispose();
    }
}
