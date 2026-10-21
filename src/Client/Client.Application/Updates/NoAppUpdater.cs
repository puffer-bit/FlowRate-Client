using Client.Application.Ports;
using Client.Core.Settings;

namespace Client.Application.Updates;

/// <summary>
/// The updater of a platform that does not update the application itself: a store or the user does.
/// </summary>
internal sealed class NoAppUpdater : IAppUpdater
{
    public bool IsSupported => false;

    public string? CurrentVersion => null;

    public Task<AppUpdate?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken = default) =>
        Task.FromResult<AppUpdate?>(null);

    public Task DownloadAsync(AppUpdate update, Action<int> progress, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This platform does not update the application itself");

    public void ApplyAndRestart(AppUpdate update) =>
        throw new NotSupportedException("This platform does not update the application itself");
}
