using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Client.Api;
using Client.Application;
using Client.GStreamer.Loader;
using Client.GStreamer.WebRTC;
using Client.Infrastructure;
using Client.LibWebRTC;
using Client.OpenAL;
using Client.Services;
using Client.Services.HotKeys;
using Client.Services.Keyring;
using Client.Services.Media;
using Client.Services.Chat;
using Client.Services.ScreenCast;
using Client.Services.Tray;
using Client.Services.Updates;
using Client.Application.Ports;
using Client.Tools.Logging;
using Client.Tools.AppInfo;
using Client.Views.Common;
using Microsoft.Extensions.DependencyInjection;
using Client.Application.Settings;
using Client.Application.Sounds;
using Microsoft.Extensions.Logging;
using Serilog;
using Client.ViewModels.Main;
using Client.ViewModels.Notifications;
using MainWindow = Client.Views.Main.MainWindow;

namespace Client;

public partial class App : global::Avalonia.Application
{
    public static IServiceProvider? Services { get; private set; }

    private TrayService? _trayService;
    
    public override void Initialize()
    {
        // The saved language is applied after the database is unlocked, before the window is shown.
        new LanguageService().Apply(null);

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Program.Main has created the logger already, so every service gets a working one from the start
        Services = InitDependencies()
            .AddClientLogging(Log.Logger)
            .BuildServiceProvider();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Subscribe before startup applies the saved window chrome.
            ExperimentalFeatures.Changed += () => WindowChrome.Apply(ExperimentalFeatures.UseCustomTitleBar);

            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowVM>(),
                Sounds = Services.GetRequiredService<ISoundService>(),
            };
            PlayErrorNotifications(Services.GetRequiredService<INotificationService>(),
                Services.GetRequiredService<ISoundService>());

            // Before the settings are read: the keys follow them once they are
            var hotKeys = Services.GetRequiredService<IHotKeysService>();
            WindowHotKeys.Attach(mainWindow, hotKeys);
            var globalHotKeys = new GlobalHotKeys(hotKeys, Services.GetRequiredService<ILogger<GlobalHotKeys>>());
            if (!LaunchOptions.Current.SafeMode)
                globalHotKeys.Start();

            desktop.Exit += (_, _) =>
            {
                globalHotKeys.Dispose();
                Services.GetRequiredService<ChatAudioSession>().Dispose();
                Services.GetRequiredService<ChatVideoSession>().Dispose();
                _trayService?.Dispose();
            };

            // Leave MainWindow unset while the lifetime starts, otherwise Avalonia shows it before
            // the database has supplied the theme, custom colors, scale and window decorations.
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await mainWindow.InitializeBeforeShowAsync(() =>
                    {
                        desktop.MainWindow = mainWindow;
                        if (!mainWindow.IsVisible)
                            mainWindow.Show();
                        _trayService ??= new TrayService(desktop, mainWindow,
                            Services.GetRequiredService<IBehaviorService>(),
                            Services.GetRequiredService<ILogger<TrayService>>());
                    });
                    // Prompts during unlock must stay visible; minimize only once initialization completes.
                    if (LaunchOptions.Current.Minimized)
                        _trayService?.HideToTray();
                }
                catch (Exception exception)
                {
                    Log.Fatal(exception, "Application initialization failed");
                    desktop.Shutdown(-1);
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    // An error in the corner of the window is heard as well as seen, unless the notifications are silenced
    private static void PlayErrorNotifications(INotificationService notifications, ISoundService sounds)
    {
        ((INotifyCollectionChanged)notifications.Notifications).CollectionChanged += (_, e) =>
        {
            if (!notifications.IsSoundMuted && e.NewItems?.OfType<ToastVM>().Any(toast => toast.IsError) == true)
                sounds.Play(SoundEvent.Error);
        };
    }

    private ServiceCollection InitDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddApplication()
            .AddApi()
            .AddInfrastructure()
            .AddScreenCast()
            .AddGStreamer()
            .AddGStreamerWebRTC()
            .AddLibWebRTC()
            .AddOpenAL();

        // The engine of each session follows the experimental setting
        services.AddSingleton<IWebRTCTransportFactory, MediaEngineTransportFactory>();
        services.AddSingleton<Client.Services.ChatMediaPlayback>();
        services.AddSingleton<ChatAudioSession>();
        services.AddSingleton<ChatVideoSession>();

        // Over the one of AddApplication, which does nothing
        if (!LaunchOptions.Current.DisableUpdates)
            services.AddSingleton<IAppUpdater, VelopackUpdater>();

        // Over the one of AddApplication, which has no keyring
        if (OperatingSystem.IsLinux())
            services.AddSingleton<ISecureStorage, SecretServiceStorage>();
        else if (OperatingSystem.IsWindows())
            services.AddSingleton<ISecureStorage, DpapiStorage>();

        services.AddViewModels();

        return services;
    }
}
