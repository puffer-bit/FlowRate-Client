namespace Client.Core.Settings;

/// <summary>
/// How much of their own log GStreamer and libwebrtc put into the journal. Off by default: from Info on
/// both write a lot, on every packet at Verbose.
/// </summary>
public enum NativeLogLevel
{
    Off = 0,
    Error = 1,
    Warning = 2,
    Info = 3,
    Debug = 4,
    Verbose = 5
}

/// <summary>
/// Whose log a <see cref="NativeLogLevel"/> is about.
/// </summary>
public enum NativeLogSource
{
    GStreamer,
    LibWebRTC
}
