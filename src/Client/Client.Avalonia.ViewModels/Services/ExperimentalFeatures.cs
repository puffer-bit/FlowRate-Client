using System;

namespace Client.Services;

/// <summary>
/// The experimental features in effect. A feature works only while the experimental features as a
/// whole are enabled; its own choice is kept meanwhile.
/// </summary>
public static class ExperimentalFeatures
{
    public static bool IsEnabled { get; private set; }

    /// <summary>
    /// Whether the main window draws its own title bar instead of the one of the system. A platform
    /// with windows applies it; see <see cref="Changed"/>.
    /// </summary>
    public static bool UseCustomTitleBar { get; private set; }

    /// <summary>
    /// Whether the voice and the screens of a session go over libwebrtc, as they do unless the
    /// experimental fallback to GStreamer is chosen or libwebrtc could not be loaded. Read when a
    /// session is joined; a platform with both engines applies it.
    /// </summary>
    public static bool UseLibWebRTC { get; private set; }

    /// <summary>
    /// Why libwebrtc could not be loaded, or null. The voice and the screens go over GStreamer then,
    /// until the application is restarted.
    /// </summary>
    public static string? LibWebRTCError { get; private set; }

    public static event Action? Changed;

    // The choice of the fallback, kept while libwebrtc being missing overrides it
    private static bool _useGStreamer;

    public static void Apply(bool isEnabled, bool useCustomTitleBar, bool useGStreamer)
    {
        _useGStreamer = useGStreamer;
        useCustomTitleBar = isEnabled && useCustomTitleBar;
        var useLibWebRTC = !(isEnabled && useGStreamer) && LibWebRTCError == null;

        if (IsEnabled == isEnabled && UseCustomTitleBar == useCustomTitleBar && UseLibWebRTC == useLibWebRTC)
            return;

        IsEnabled = isEnabled;
        UseCustomTitleBar = useCustomTitleBar;
        UseLibWebRTC = useLibWebRTC;
        Changed?.Invoke();
    }

    /// <summary>
    /// Takes the voice and the screens to GStreamer, as libwebrtc could not be loaded. Called on the
    /// UI thread, as <see cref="Changed"/> is raised there.
    /// </summary>
    public static void ReportLibWebRTCFailed(string error)
    {
        LibWebRTCError = error;
        Apply(IsEnabled, UseCustomTitleBar, _useGStreamer);
    }
}
