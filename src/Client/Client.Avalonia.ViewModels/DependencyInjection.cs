using System;
using Client.Application.Chat;
using Client.Application.Coordinator.Users;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Sessions.Hybrid;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Client.ViewModels.Connections;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.List;
using Client.ViewModels.Settings;

namespace Client;

public static class DependencyInjection
{
    /// <summary>
    /// The view models and the services of the interface every platform shares. The platform
    /// registers the ports they use (the application layer, storage, media) itself.
    /// </summary>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationLoader>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ILanguageService, LanguageService>();
        services.AddSingleton<ISoundPack, BuiltInSoundPack>();
        services.AddSingleton<IChatImageService, ChatImageService>();
        services.AddSingleton<IAvatarService, AvatarService>();
        services.AddSingleton<IImagePreviewer>(provider => provider.GetRequiredService<IChatImageService>());

        services.AddSingleton(provider => new ChatSessionServices(
            provider.GetRequiredService<IChatCacheStore>(),
            provider.GetRequiredService<IChatMediaUploader>(),
            provider.GetRequiredService<IChatImageService>(),
            provider.GetRequiredService<IDismissedPromptsRepository>()));
        services.AddSingleton(provider => new HybridSessionServices(
            provider.GetService<Func<IScreenCastClient>>(),
            provider.GetRequiredService<IAudioPlayerService>(),
            provider.GetRequiredService<IAudioService>(),
            provider.GetRequiredService<ISoundService>(),
            provider.GetRequiredService<INotificationService>(),
            provider.GetRequiredService<IVoiceActivityService>(),
            provider.GetRequiredService<IScreenWatchService>(),
            provider.GetRequiredService<IPeerPlaybackService>(),
            provider.GetRequiredService<IHybridSessionFactory>(),
            provider.GetService<IScreenSourceProvider>(),
            provider.GetService<IAudioAppProvider>(),
            provider.GetRequiredService<IMediaCapabilities>(),
            provider.GetRequiredService<IScreenShareSettingsRepository>(),
            provider.GetRequiredService<IDismissedPromptsRepository>(),
            provider.GetRequiredService<ICoordinatorStateRepository>()));
        // Resolved when a hybrid session is entered, see HybridSessionServices
        services.AddSingleton<Func<HybridSessionServices>>(provider => provider.GetRequiredService<HybridSessionServices>);
        services.AddSingleton<LastSessionKeeper>();
        services.AddSingleton<SessionListPreferences>();
        services.AddSingleton<UpdaterVM>();

        services.AddTransient<MainWindowVM>();
        services.AddTransient<ConnectWindowVM>();
        services.AddSingleton<Func<ConnectWindowVM>>(provider => provider.GetRequiredService<ConnectWindowVM>);
        // Created outside the container, which would keep every disposable view model it resolves
        // until the application exits
        services.AddSingleton<Func<SettingsWindowVM>>(provider => () => new SettingsWindowVM(
            provider.GetRequiredService<IAccountingService>(),
            provider.GetRequiredService<IDatabaseSecurityService>(),
            provider.GetRequiredService<IAppearanceService>(),
            provider.GetRequiredService<IBehaviorService>(),
            provider.GetRequiredService<IStorageService>(),
            provider.GetRequiredService<IVideoService>(),
            provider.GetRequiredService<IMediaCapabilities>(),
            provider.GetRequiredService<IAudioService>(),
            provider.GetRequiredService<IMicrophoneTester>(),
            provider.GetRequiredService<ISoundService>(),
            provider.GetRequiredService<ISoundPackCatalog>(),
            provider.GetRequiredService<IDebugAndLogService>(),
            provider.GetRequiredService<IPlatformSpecificService>(),
            provider.GetRequiredService<IHotKeysService>(),
            provider.GetRequiredService<IThemeService>(),
            provider.GetRequiredService<ILanguageService>(),
            provider.GetRequiredService<ICollapsedSectionsRepository>()));

        return services;
    }
}
