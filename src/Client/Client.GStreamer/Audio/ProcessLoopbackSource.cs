using Gst;
using Client.Application.Ports;

namespace Client.GStreamer.Audio;

/// <summary>
/// Captures what a process tree plays on Windows, or everything but it, through the process
/// loopback of the audio engine. It is taken before any device, so the volume of the device and a
/// switch to another one do not change it.
/// </summary>
public static class ProcessLoopbackSource
{
    private const string ElementName = "wasapi2src";

    // Earlier builds only offer device loopback, which would also capture our own playback.
    private const int MinimumBuild = 20348;

    public static bool IsAvailable
    {
        get
        {
            using var source = CreateSupportedSource();
            return source != null;
        }
    }

    private static Element? CreateSupportedSource()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumBuild))
            return null;

        var source = ElementFactory.Make(ElementName, null);

        // Process loopback properties are conditional and may be absent in the loaded plugin.
        // Never fall back to device loopback when process filtering is unavailable.
        if (source == null ||
            (source.FindProperty("loopback-target-pid") != null && source.FindProperty("loopback-mode") != null))
            return source;

        source.Dispose();
        return null;
    }

    /// <returns>Null where the process loopback is not available.</returns>
    public static Element? Create(ProcessLoopbackAudioInput input)
    {
        if (input.ProcessId == 0)
            return null;

        var source = CreateSupportedSource();

        if (source == null)
            return null;

        source.SetProperty("loopback", true);
        source.SetProperty("loopback-target-pid", input.ProcessId);
        Global.UtilSetObjectArg(source, "loopback-mode", input.IsExcluded ? "exclude-process-tree" : "include-process-tree");

        return source;
    }
}
