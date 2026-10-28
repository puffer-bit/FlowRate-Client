using Client.Core.Media;
using Client.Application.Ports;
using Client.GStreamer.Audio;

namespace Client.GStreamer.Video;

public class GstMediaCapabilities : IMediaCapabilities
{
    // Screen sharing encodes H.264
    public bool HasHardwareScreenShareEncoder => VideoEncoderSelector.HasHardwareEncoder(VideoCodec.H264);

    public IReadOnlyList<string> ScreenShareDecoders => VideoDecoderSelector.GetAvailableDecoders(VideoCodec.H264);

    public IReadOnlyList<string> ScreenShareEncoders => VideoEncoderSelector.GetAvailableEncoders(VideoCodec.H264);

    public bool CanCaptureScreenAudio => OperatingSystem.IsWindows()
        ? ProcessLoopbackSource.IsAvailable
        : PipeWireAppAudioSource.IsAvailable;
}
