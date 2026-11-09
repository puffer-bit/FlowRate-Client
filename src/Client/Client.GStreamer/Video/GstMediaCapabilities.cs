using Client.Core.Media;
using Client.Application.Ports;
using Client.GStreamer.Audio;

namespace Client.GStreamer.Video;

public class GstMediaCapabilities : IMediaCapabilities
{
    public IReadOnlyList<Client.Core.Settings.CodecParameter> GetCodecParameters(string factory) => CodecParameters.Get(factory);

    public IReadOnlyList<string> GetScreenShareEncoders(VideoCodec codec) =>
        MediaRuntime.IsEnabled ? VideoEncoderSelector.GetAvailableEncoders(codec) : [];

    public IReadOnlyList<string> GetScreenShareDecoders(VideoCodec codec) =>
        MediaRuntime.IsEnabled ? VideoDecoderSelector.GetAvailableDecoders(codec) : [];

    // H.264 is the default screen sharing codec.
    public bool HasHardwareScreenShareEncoder => MediaRuntime.IsEnabled && VideoEncoderSelector.HasHardwareEncoder(VideoCodec.H264);

    public IReadOnlyList<string> ScreenShareDecoders => GetScreenShareDecoders(VideoCodec.H264);

    public IReadOnlyList<string> ScreenShareAv1Decoders => GetScreenShareDecoders(VideoCodec.AV1);

    public IReadOnlyList<string> ScreenShareEncoders => GetScreenShareEncoders(VideoCodec.H264);

    public IReadOnlyList<string> ScreenShareAv1Encoders => GetScreenShareEncoders(VideoCodec.AV1);

    public bool CanCaptureScreenAudio => MediaRuntime.IsEnabled && (OperatingSystem.IsWindows()
        ? ProcessLoopbackSource.IsAvailable
        : PipeWireAppAudioSource.IsAvailable);
}
