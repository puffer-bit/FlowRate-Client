namespace Client.GStreamer;

/// <summary>
/// What the host decides for GStreamer, set once at start before the runtime is loaded.
/// </summary>
public static class GStreamerSettings
{
    /// <summary>
    /// Where the bundled runtime keeps its plugin registry and the empty folder of GIO modules
    /// between runs. The host puts it into its data folder; by default it is in the temp directory.
    /// </summary>
    public static string CacheDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "FlowRate", "gstreamer");

    /// <summary>
    /// Whether the video encoders and decoders may use the GPU. Off, only the software ones are taken.
    /// </summary>
    public static bool AllowHardwareCodecs { get; set; } = true;
}
