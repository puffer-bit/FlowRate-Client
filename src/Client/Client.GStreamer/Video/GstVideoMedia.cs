using Client.Application.Ports;
using Client.Core.Media;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Video;

/// <summary>
/// Shared screens on GStreamer, for any transport: the capture and the encoders of the system, and
/// its decoders with frames on the GPU where the screen can show them.
/// </summary>
public sealed class GstVideoMedia(ILoggerFactory loggerFactory) : IVideoMedia
{
    private readonly ILogger _captureLogger = loggerFactory.CreateLogger<GstScreenCapture>();
    private readonly ILogger _decoderLogger = loggerFactory.CreateLogger<GstVideoDecoder>();

    public IScreenCapture? CreateCapture(VideoInput input, ScreenShareSettings settings, VideoCodec codec, string name) =>
        GstScreenCapture.Create(input, settings, codec, name, _captureLogger);

    public IVideoDecoder? CreateDecoder(VideoCodec codec, string name) =>
        GstVideoDecoder.Create(codec, name, _decoderLogger);
}
