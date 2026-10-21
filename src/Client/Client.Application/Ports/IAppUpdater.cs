using Client.Core.Settings;

namespace Client.Application.Ports;

/// <summary>
/// Updates the application from its releases. The platform that installs the application provides it;
/// elsewhere, as on Android or in a build run from the sources, <see cref="IsSupported"/> is false.
/// </summary>
public interface IAppUpdater
{
    /// <summary>
    /// Whether this copy of the application can update itself: it was installed from a release.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// The version of this copy, as the releases name it, or null when it is not installed from one.
    /// </summary>
    string? CurrentVersion { get; }

    /// <summary>
    /// Asks for a newer release. Throws when the releases cannot be reached.
    /// </summary>
    /// <param name="channel">
    /// Which releases count. A development build looking at the releases alone is offered the latest
    /// release even when its version is lower, so that it can go back to them.
    /// </param>
    /// <returns>The newer release, or null when this copy is the latest one.</returns>
    Task<AppUpdate?> CheckAsync(UpdateChannel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a release found by <see cref="CheckAsync"/> and prepares it to be applied.
    /// </summary>
    /// <param name="progress">Percents downloaded, 0 to 100, reported from any thread.</param>
    Task DownloadAsync(AppUpdate update, Action<int> progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the application, puts the downloaded release in its place and starts it again.
    /// </summary>
    void ApplyAndRestart(AppUpdate update);
}

/// <summary>
/// A newer release.
/// </summary>
/// <param name="Version">Its version, "0.6.0".</param>
/// <param name="Size">The size of the download in bytes: of the changes alone where they can be applied.</param>
/// <param name="Notes">What is new in it, Markdown; null when the release tells nothing.</param>
public sealed record AppUpdate(string Version, long Size, string? Notes);
