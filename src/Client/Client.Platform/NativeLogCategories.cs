namespace Client.Platform;

/// <summary>
/// The logger categories the native libraries write their own log under. The libraries that pass
/// the log on and the host that routes it (to files of their own, at their own level) share them.
/// </summary>
public static class NativeLogCategories
{
    /// <summary>
    /// The beginning of every native category: the native libraries choose themselves how much they say.
    /// </summary>
    public const string Prefix = "Native";

    /// <summary>
    /// The log of GStreamer itself.
    /// </summary>
    public const string GStreamer = "Native.GStreamer";

    /// <summary>
    /// The log of libwebrtc itself.
    /// </summary>
    public const string LibWebRTC = "Native.LibWebRTC";
}
