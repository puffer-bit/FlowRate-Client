using Gst;
using Client.Application.Ports;

namespace Client.GStreamer.Audio;

/// <summary>
/// Captures what a process tree plays on Windows, or everything but it, through the process
/// loopback of the audio engine. It is taken before any device, so the volume of the device and a
/// switch to another one do not change it.
/// </summary>
internal static class ProcessLoopbackSource
{
    private const string ElementName = "wasapi2src";

    // The engine offers the process loopback from Windows 10 2004 on
    private const int MinimumBuild = 19041;

    public static bool IsAvailable =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumBuild) && ElementFactory.Find(ElementName) != null;

    /// <returns>Null where the process loopback is not available.</returns>
    public static Element? Create(ProcessLoopbackAudioInput input)
    {
        if (!IsAvailable || input.ProcessId == 0)
            return null;

        var source = ElementFactory.Make(ElementName, null);

        if (source == null)
            return null;

        source.SetProperty("loopback", true);
        source.SetProperty("loopback-target-pid", input.ProcessId);
        Global.UtilSetObjectArg(source, "loopback-mode", input.IsExcluded ? "exclude-process-tree" : "include-process-tree");

        return source;
    }
}
