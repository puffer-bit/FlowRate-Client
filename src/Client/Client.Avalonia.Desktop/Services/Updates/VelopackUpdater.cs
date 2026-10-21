using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.Tools.AppInfo;
using Velopack;
using Velopack.Sources;

namespace Client.Services.Updates;

/// <summary>
/// Updates the application from the GitHub releases of its repository with Velopack. Only a copy
/// installed from a release can update itself: the Windows installer puts it in
/// %LocalAppData%\FlowRateClient, on Linux it runs as an AppImage. The release is looked for in the
/// channel it was packed in, so a JIT build stays JIT and an AOT build stays AOT. Development builds
/// are GitHub pre-releases with versions like "0.6.0-dev.2610211130".
/// </summary>
internal sealed class VelopackUpdater : IAppUpdater
{
    /// <summary>
    /// Names a folder or an address with releases to take instead of GitHub: a release is tried out
    /// before it is published.
    /// </summary>
    private const string SourceVariable = "FLOWRATE_UPDATE_SOURCE";

    // What this copy is; the checks make managers of their own for the releases they look at
    private readonly UpdateManager _installed = Create(UpdateChannel.Release, null);

    // The manager of the last check and what it found: the download and the restart take them
    private UpdateManager? _manager;
    private UpdateInfo? _found;

    public bool IsSupported => _installed.IsInstalled;

    public string? CurrentVersion => _installed.CurrentVersion?.ToString();

    public async Task<AppUpdate?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        // A development build going back to the releases takes the latest one, though its version is lower
        var options = new UpdateOptions
        {
            AllowVersionDowngrade = channel == UpdateChannel.Release && _installed.CurrentVersion?.IsPrerelease == true
        };
        var manager = Create(channel, options);

        var found = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        (_manager, _found) = (manager, found);
        if (found == null)
            return null;

        var target = found.TargetFullRelease;
        // The changes alone are downloaded when there is a chain of them from this version
        var size = found.DeltasToTarget.Length > 0 ? found.DeltasToTarget.Sum(delta => delta.Size) : target.Size;
        return new AppUpdate(target.Version.ToString(), size,
            string.IsNullOrWhiteSpace(target.NotesMarkdown) ? null : target.NotesMarkdown);
    }

    public Task DownloadAsync(AppUpdate update, Action<int> progress, CancellationToken cancellationToken = default)
    {
        var (manager, found) = Found(update);
        return manager.DownloadUpdatesAsync(found, progress, cancellationToken);
    }

    public void ApplyAndRestart(AppUpdate update)
    {
        var (manager, found) = Found(update);

        // The updater waits for this process to end, so the application closes the usual way: the
        // voice session is left and the logs are written out before the files are replaced
        manager.WaitExitThenApplyUpdates(found.TargetFullRelease, silent: false, restart: true);
        Dispatcher.UIThread.Post(() =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }

    private static UpdateManager Create(UpdateChannel channel, UpdateOptions? options) =>
        Environment.GetEnvironmentVariable(SourceVariable) is { Length: > 0 } source
            ? new UpdateManager(source, options)
            : new UpdateManager(new GithubSource(AppInformation.RepositoryUrl, null, channel == UpdateChannel.Dev), options);

    private (UpdateManager Manager, UpdateInfo Found) Found(AppUpdate update) =>
        _manager is { } manager && _found is { } found && found.TargetFullRelease.Version.ToString() == update.Version
            ? (manager, found)
            : throw new InvalidOperationException($"Update {update.Version} was not found by the last check");
}
