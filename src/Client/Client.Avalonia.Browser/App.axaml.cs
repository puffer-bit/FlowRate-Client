using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Client.Api;
using Client.Application;
using Client.Application.Sounds;
using Client.Browser.Platform;
using Client.Infrastructure;
using Client.Platform;
using Client.Services;
using Client.Tools.AppInfo;
using Client.Tools.Logging;
using Client.ViewModels.Main;
using Client.Views.Common;
using Client.Views.Dialogs;
using Client.Views.Main;
using Client.Views.Shared.Common;
using Client.Views.Shared.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Client.Browser;

public partial class App : global::Avalonia.Application
{
    // The file system of the page lives in memory: the database and the cache are gone on reload
    private const string DataDirectory = "/flowrate";

    public override void Initialize()
    {
        new LanguageService().Apply(null);

        AvaloniaXamlLoader.Load(this);
        ApplicationFontResources.Initialize(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var services = InitDependencies()
                .AddClientLogging(Log.Logger)
                .BuildServiceProvider();

            // What the views created by markup take from the application
            ViewServices.Provider = services;
            DialogPresenter.Current = new WindowDialogPresenter();
            DesktopHost.Current = new BrowserHost();

            // The page is the main view, as the window is on the desktop
            var mainView = new MainView
            {
                DataContext = services.GetRequiredService<MainWindowVM>(),
                Sounds = services.GetRequiredService<ISoundService>()
            };
            singleView.MainView = mainView;

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await mainView.InitializeBeforeShowAsync(() => { });
                }
                catch (Exception exception)
                {
                    Log.Fatal(exception, "Application initialization failed");
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceCollection InitDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddApplication(DataDirectory)
            .AddApi(new ClientInfo("Browser", DevicePlatform.Name, AppInformation.GetClientVersion(),
                Environment.OSVersion.VersionString, "wasm"))
            .AddInfrastructure(DataDirectory)
            .AddBrowserPlatform();

        services.AddViewModels();

        return services;
    }
}
