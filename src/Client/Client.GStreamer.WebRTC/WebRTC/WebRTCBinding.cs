using Client.GStreamer.Loader;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// The modules of the binding only the transport uses. A module that is never initialized hands its
/// objects out as plain elements and boxed values.
/// </summary>
internal static class WebRTCBinding
{
    private static readonly Lazy<bool> Initialized = new(Initialize);

    /// <summary>Initializes the modules once; false while the runtime is not loaded.</summary>
    public static bool EnsureInitialized() => Initialized.Value;

    private static bool Initialize()
    {
        if (GstLoader.Options is not { } options)
            return false;

        Gst.Sdp.GstSdp.Initialize(options);
        Gst.WebRTC.GstWebRTC.Initialize(options);
        return true;
    }
}
