using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Audio;

/// <summary>
/// Sound captured on GStreamer as samples, for a transport that encodes it itself: the process
/// loopback of Windows, the streams of an application on PipeWire, a file or a tone.
/// </summary>
public sealed class GstAudioMedia(ILoggerFactory loggerFactory) : IAudioMedia
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<GstAudioCapture>();

    public IAudioCapture? CreateCapture(AudioInput input, string name) => GstAudioCapture.Create(input, name, _logger);
}
