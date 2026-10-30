using System;
using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Client.Api;
using Client.Application;
using Client.Avalonia.Localization;
using Client.Droid.Platform;
using Client.Droid.Views;
using Client.Infrastructure;
using Client.Tools.Logging;
using Client.ViewModels.Main;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Client.Droid;

public partial class App : global::Avalonia.Application
{
    public override void Initialize()
    {
        Strings.SetLanguage(CultureInfo.CurrentUICulture.Name);

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Android has no windows: the application shows one root view
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var services = InitDependencies()
                .AddClientLogging(Log.Logger)
                .BuildServiceProvider();

            singleView.MainView = new MainView(services.GetRequiredService<MainWindowVM>(), services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceCollection InitDependencies()
    {
        var services = new ServiceCollection();

        services
            // The place of the executable is read-only on Android; this is the data directory of the application
            .AddApplication(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .AddApi()
            .AddInfrastructure(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .AddAndroidPlatform();

        services.AddViewModels();

        return services;
    }
}
