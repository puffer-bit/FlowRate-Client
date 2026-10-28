namespace Client.Application.Ports;

/// <summary>
/// The libwebrtc of this machine, which the voice goes over when the experimental setting picks it.
/// </summary>
public interface ILibWebRTCLoader
{
    /// <summary>
    /// Loads libwebrtc, once per process; may take a while the first time, as it opens the audio
    /// devices. A failure stays until the application is restarted.
    /// </summary>
    /// <returns>Null once it is loaded, or why it could not be.</returns>
    string? Load();
}
