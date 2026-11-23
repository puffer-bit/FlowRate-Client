using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Database.Sqlite.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Database.Sqlite;

public static class DependencyInjection
{
    /// <summary>Registers SQLite storage in the supplied data directory.</summary>
    public static IServiceCollection AddSqliteDatabase(this IServiceCollection services, string databaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);

        services.AddSingleton(SqliteDatabase.In(databaseDirectory));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IDatabaseProtection, DatabaseProtection>();

        services.AddSingleton<IAccountingSettingsRepository, AccountingSettingsRepository>();
        services.AddSingleton<ISecretsRepository, SecretsRepository>();
        services.AddSingleton<IAppearanceSettingsRepository, AppearanceSettingsRepository>();
        services.AddSingleton<IBehaviorSettingsRepository, BehaviorSettingsRepository>();
        services.AddSingleton<IAudioSettingsRepository, AudioSettingsRepository>();
        services.AddSingleton<IScreenShareSettingsRepository, ScreenShareSettingsRepository>();
        services.AddSingleton<ICameraPreferencesRepository, CameraPreferencesRepository>();
        services.AddSingleton<IChatAttachmentPreferencesRepository, ChatAttachmentPreferencesRepository>();
        services.AddSingleton<ICollapsedSectionsRepository, CollapsedSectionsRepository>();
        services.AddSingleton<IDismissedPromptsRepository, DismissedPromptsRepository>();
        services.AddSingleton<IRememberedUsersRepository, RememberedUsersRepository>();
        services.AddSingleton<IUserNotesRepository, UserNotesRepository>();
        services.AddSingleton<ITrustedCertificatesRepository, TrustedCertificatesRepository>();
        services.AddSingleton<IChatCacheRepository, ChatCacheRepository>();
        services.AddSingleton<IMutedChatsRepository, MutedChatsRepository>();
        services.AddSingleton<IChatReadStateRepository, ChatReadStateRepository>();
        services.AddSingleton<ICoordinatorStateRepository, CoordinatorStateRepository>();
        services.AddSingleton<IPushKeysRepository, PushKeysRepository>();
        services.AddSingleton<IStorageSettingsRepository, StorageSettingsRepository>();
        services.AddSingleton<IVideoSettingsRepository, VideoSettingsRepository>();
        services.AddSingleton<IDebugSettingsRepository, DebugSettingsRepository>();
        services.AddSingleton<IHotKeysSettingsRepository, HotKeysSettingsRepository>();

        return services;
    }
}
