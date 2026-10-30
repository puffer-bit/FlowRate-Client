using Client.Core.Settings;

namespace Client.Application.Ports;

/// <summary>
/// The log of a native library, GStreamer or libwebrtc, passed into the journal of the application.
/// </summary>
public interface INativeLog
{
    NativeLogSource Source { get; }

    /// <summary>
    /// How much of its log the library passes from now on. A library that is not loaded yet takes it
    /// when it is.
    /// </summary>
    void SetLevel(NativeLogLevel level);
}
