using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Infrastructure.Database;
using Client.Infrastructure.Database.Repositories;
using Client.Infrastructure.Media;
using Client.Tools.AppInfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Client.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The local storage: the database and the repositories over it.
    /// </summary>
    /// <param name="databaseDirectory">
    /// Where the database file and the media cache lie. Null puts them in <see cref="AppDirectories.Data"/>,
    /// which suits the desktop; a platform with a data folder of its own names it.
    /// </param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? databaseDirectory = null)
    {
        services.AddSingleton(databaseDirectory == null ? new SqliteDatabase() : SqliteDatabase.In(databaseDirectory));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IDatabaseProtection, DatabaseProtection>();

        services.AddSingleton<IAccountingSettingsRepository, AccountingSettingsRepository>();
        services.AddSingleton<IAppearanceSettingsRepository, AppearanceSettingsRepository>();
        services.AddSingleton<IBehaviorSettingsRepository, BehaviorSettingsRepository>();
        services.AddSingleton<IAudioSettingsRepository, AudioSettingsRepository>();
        services.AddSingleton<IScreenShareSettingsRepository, ScreenShareSettingsRepository>();
        services.AddSingleton<ICollapsedSectionsRepository, CollapsedSectionsRepository>();
        services.AddSingleton<IDismissedPromptsRepository, DismissedPromptsRepository>();
        services.AddSingleton<IRememberedUsersRepository, RememberedUsersRepository>();
        services.AddSingleton<ITrustedCertificatesRepository, TrustedCertificatesRepository>();
        services.AddSingleton<IChatCacheRepository, ChatCacheRepository>();
        services.AddSingleton<IMutedChatsRepository, MutedChatsRepository>();
        services.AddSingleton<ICoordinatorStateRepository, CoordinatorStateRepository>();
        services.AddSingleton<IPushKeysRepository, PushKeysRepository>();
        services.AddSingleton<IStorageSettingsRepository, StorageSettingsRepository>();
        services.AddSingleton<IVideoSettingsRepository, VideoSettingsRepository>();
        services.AddSingleton<IHotKeysSettingsRepository, HotKeysSettingsRepository>();
        services.AddSingleton<IMediaCache>(provider => new MediaCache(
            Path.Combine(databaseDirectory ?? AppDirectories.Data, MediaCache.FolderName),
            provider.GetRequiredService<ILogger<MediaCache>>()));

        return services;
    }
}
