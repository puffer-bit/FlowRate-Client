using Client.Application.Chat;
using Client.Application.Connectivity;
using Client.Application.Coordinator.Users;
using Microsoft.Extensions.DependencyInjection;
using Client.Application.Ports;
using Client.Application.Coordinator;
using Client.Application.Push;
using Client.Application.Sessions.Hybrid;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.Application.Updates;
using Client.Tools.AppInfo;

namespace Client.Application;

public static class DependencyInjection
{
    /// <param name="dataDirectory">Where the files of the user are kept. Null means
    /// <see cref="AppDirectories.Data"/>; a platform with a data folder of its own names it.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services, string? dataDirectory = null)
    {
        services.AddSingleton<IBehaviorService, BehaviorService>();
        services.AddSingleton<IAudioService, AudioService>();
        services.AddSingleton<IAccountingService, AccountingService>();
        services.AddSingleton<IAppearanceService, AppearanceService>();
        services.AddSingleton<IDebugAndLogService, DebugAndLogService>();
        services.AddSingleton<IHotKeysService, HotKeysService>();
        services.AddSingleton<IPlatformSpecificService, PlatformSpecificService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<IVideoService, VideoService>();
        services.AddSingleton<IDatabaseSecurityService, DatabaseSecurityService>();

        // A platform with a keyring registers its own after this one
        services.AddSingleton<ISecureStorage, NoSecureStorage>();

        // A platform that updates the application itself registers its updater after this one
        services.AddSingleton<IAppUpdater, NoAppUpdater>();

        // A platform with a network service of its own registers its monitor after this one
        services.AddSingleton<IConnectivityMonitor, SystemConnectivityMonitor>();

        services.AddSingleton<RememberedUsersRecorder>();
        services.AddSingleton<SavedAccountsRecorder>();
        services.AddSingleton<PushRegistration>();
        services.AddTransient<PushReplies>();
        services.AddSingleton<IPeerPlaybackService, PeerPlaybackService>();
        services.AddSingleton<IVoiceActivityService, VoiceActivityService>();
        services.AddSingleton<IScreenWatchService, ScreenWatchService>();
        services.AddSingleton<ICoordinatorFactory, CoordinatorFactory>();
        services.AddSingleton<IChatCacheStore, ChatCacheStore>();
        services.AddSingleton<IChatMediaUploader, ChatMediaUploader>();
        services.AddSingleton<IHybridSessionFactory, HybridSessionFactory>();
        services.AddSingleton<ISoundPackCatalog>(new DirectorySoundPackCatalog(
            Path.Combine(dataDirectory ?? AppDirectories.Data, DirectorySoundPackCatalog.FolderName)));
        services.AddSingleton<ISoundService, SoundService>();

        return services;
    }
}
