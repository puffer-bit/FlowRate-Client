namespace Client.Application.Ports;

/// <summary>
/// An application that plays sound right now.
/// </summary>
/// <param name="Key">Stays the same for the application across its streams and restarts.</param>
/// <param name="Name">What the application calls itself, to be shown to the user.</param>
/// <param name="StreamSerials">The serials of its playback streams in the sound server.</param>
public sealed record AudioApp(string Key, string Name, IReadOnlyList<uint> StreamSerials);

/// <summary>
/// The applications that play sound, where the sound of a single one can be captured: on Linux,
/// those with a playback stream in PipeWire. Our own application is left out.
/// </summary>
public interface IAudioAppProvider
{
    /// <summary>
    /// The applications as they were last seen, sorted by name. Current only while watched.
    /// </summary>
    IReadOnlyList<AudioApp> Apps { get; }

    /// <summary>
    /// Raised when an application or a stream of one came or went. Raised on any thread.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Keeps <see cref="Apps"/> current until the result is disposed. Watches may overlap, the
    /// list is followed while any of them is held.
    /// </summary>
    IDisposable Watch();
}
