namespace Client.Application.Ports;

/// <summary>
/// The libwebrtc of this machine, which the voice and the screens go over unless the experimental
/// fallback to GStreamer is chosen.
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
