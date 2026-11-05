using Client.Core.Media;
using Client.Core.Settings;

namespace Client.Application.Ports;

/// <summary>Fills one 10 ms frame of 48 kHz stereo S16 PCM for playback.</summary>
public delegate void AudioPlayoutHandler(Span<short> samples);

/// <summary>
/// The desktop audio device shared by the native transports. Playback is pulled by the media
/// clock after the transport has decoded and concealed packet loss; capture is already processed.
/// </summary>
public interface IVoiceAudioDevice
{
    event Action<string>? Failed;
    bool StartPlayback(AudioPlayoutHandler read);
    void StopPlayback();
    IProcessedMicrophone? CreateMicrophone(AudioInput input, double volume, NoiseSuppressionLevel suppression,
        VoiceGateOptions? gate, bool echoCancellation, bool autoGainControl);
}

/// <summary>Processed mono S16 PCM at 48 kHz. Samples are valid only during the callback.</summary>
public interface IProcessedMicrophone : IDisposable
{
    event AudioSamplesHandler? SamplesReady;
    bool Start();
    void SetVolume(double volume);
    void SetVoiceGate(VoiceGateOptions? options);
    double TakeLevel();
    VoiceGateSummary? TakeGateSummary();
}
