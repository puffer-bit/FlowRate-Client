using System.Runtime.Versioning;
using Client.Application.Ports;
using Client.GStreamer.Video.Mac;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// The sound of a share on macOS, captured by ScreenCaptureKit: what one application plays, or
/// everything but it. The counterpart of the process loopback of Windows, so it takes the same input.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class ScreenCaptureKitAudioSource : IDisposable
{
    // A late piece of the sound is worth nothing, so the oldest gives way rather than the queue growing
    private static readonly (string Name, string Value)[] Properties =
    [
        ("is-live", "true"), ("do-timestamp", "true"), ("format", "time"), ("max-buffers", "20"),
        ("max-bytes", "0"), ("leaky-type", "downstream"), ("caps", ScreenCaptureKitCapture.AudioCaps)
    ];

    private readonly ScreenCaptureKitCapture _capture;

    private ScreenCaptureKitAudioSource(Element element, ScreenCaptureKitCapture capture)
    {
        Element = element;
        _capture = capture;
    }

    public Element Element { get; }

    public static bool IsAvailable => ScreenCaptureKit.IsAvailable;

    /// <returns>Null when the library is missing or the system did not start the capture.</returns>
    public static ScreenCaptureKitAudioSource? Create(ProcessLoopbackAudioInput input, Action<Exception> onError)
    {
        if (input.ProcessId == 0 || !IsAvailable)
            return null;

        var source = ElementFactory.Make("appsrc", null);

        if (source == null)
            return null;

        foreach (var (name, value) in Properties)
            Global.UtilSetObjectArg(source, name, value);

        try
        {
            var capture = ScreenCaptureKitCapture.Start(source, new ScreenCaptureKitNative.Config
            {
                Kind = ScreenCaptureKitNative.KindAudio,
                AudioPid = input.ProcessId,
                AudioExclude = input.IsExcluded ? 1 : 0
            }, onError);

            return new ScreenCaptureKitAudioSource(source, capture);
        }
        catch (InvalidOperationException exception)
        {
            onError(exception);
            source.Dispose();
            return null;
        }
    }

    public void Dispose() => _capture.Dispose();
}
