using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
using Client.Services.ScreenCast;
using Client.Services.Tray;
using Client.Services.Updates;
using Client.Application.Ports;
using Client.Tools.Logging;
using Client.Views.Common;
#if DEBUG
using HotAvalonia;
#endif
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
        // The saved language is in the database, which is read after the main window is shown
        new LanguageService().Apply(null);

        AvaloniaXamlLoader.Load(this);
#if DEBUG
        if (Debugger.IsAttached)
        {
            this.UseHotReload(); 
        }
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Program.Main has created the logger already, so every service gets a working one from the start
        Services = InitDependencies()
            .AddClientLogging(Log.Logger)
            .BuildServiceProvider();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Before the main window: the features are applied while it is already shown
            ExperimentalFeatures.Changed += () => WindowChrome.Apply(ExperimentalFeatures.UseCustomTitleBar);

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowVM>(),
                Sounds = Services.GetRequiredService<ISoundService>(),
            };
            PlayErrorNotifications(Services.GetRequiredService<INotificationService>(),
                Services.GetRequiredService<ISoundService>());
            _trayService = new TrayService(desktop, desktop.MainWindow, Services.GetRequiredService<IBehaviorService>(),
                Services.GetRequiredService<ILogger<TrayService>>());

            // Before the settings are read: the keys follow them once they are
            var hotKeys = Services.GetRequiredService<IHotKeysService>();
            WindowHotKeys.Attach(desktop.MainWindow, hotKeys);
            var globalHotKeys = new GlobalHotKeys(hotKeys, Services.GetRequiredService<ILogger<GlobalHotKeys>>());
            globalHotKeys.Start();

            desktop.Exit += (_, _) =>
            {
                globalHotKeys.Dispose();
                _trayService.Dispose();
            };
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

        // Over the one of AddApplication, which does nothing
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
