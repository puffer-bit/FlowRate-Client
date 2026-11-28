using Client.Application.Ports;
using Client.Database.Sqlite;
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
        services.AddSqliteDatabase(databaseDirectory ?? AppDirectories.Data);
        services.AddSingleton<IReleaseNotesSource, Updates.GitHubReleaseNotesSource>();
        services.AddSingleton<IMediaCache>(provider => new MediaCache(
            Path.Combine(databaseDirectory ?? AppDirectories.Data, MediaCache.FolderName),
            provider.GetRequiredService<ILogger<MediaCache>>()));

        return services;
    }
}
