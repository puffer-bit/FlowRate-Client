using Client.Core.Media;
using Client.Application.Ports;
using Client.GStreamer.Audio;

namespace Client.GStreamer.Video;

public class GstMediaCapabilities : IMediaCapabilities
{
    public IReadOnlyList<Client.Core.Settings.EncoderParameter> GetEncoderParameters(string encoder) => EncoderParameters.Get(encoder);

    // Screen sharing encodes H.264
    public bool HasHardwareScreenShareEncoder => MediaRuntime.IsEnabled && VideoEncoderSelector.HasHardwareEncoder(VideoCodec.H264);

    public IReadOnlyList<string> ScreenShareDecoders => MediaRuntime.IsEnabled ? VideoDecoderSelector.GetAvailableDecoders(VideoCodec.H264) : [];

    public IReadOnlyList<string> ScreenShareAv1Decoders => MediaRuntime.IsEnabled ? VideoDecoderSelector.GetAvailableDecoders(VideoCodec.AV1) : [];

    public IReadOnlyList<string> ScreenShareEncoders => MediaRuntime.IsEnabled ? VideoEncoderSelector.GetAvailableEncoders(VideoCodec.H264) : [];

    public IReadOnlyList<string> ScreenShareAv1Encoders => MediaRuntime.IsEnabled ? VideoEncoderSelector.GetAvailableEncoders(VideoCodec.AV1) : [];

    public bool CanCaptureScreenAudio => MediaRuntime.IsEnabled && (OperatingSystem.IsWindows()
        ? ProcessLoopbackSource.IsAvailable
        : PipeWireAppAudioSource.IsAvailable);
}
