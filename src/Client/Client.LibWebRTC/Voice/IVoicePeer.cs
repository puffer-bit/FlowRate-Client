using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Settings;
using Shared.Enums.WebRTC;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// The peer connection of one voice session on top of libwebrtc, which owns ICE, DTLS-SRTP, the
/// microphone and the playback. <see cref="VoiceTransport"/> builds the description of the SFU and
/// keeps the sections; a peer only answers it, so each platform needs nothing of libwebrtc but this.
/// </summary>
/// <remarks>The calls of <see cref="IVoicePeerListener"/> come on the threads of libwebrtc.</remarks>
public interface IVoicePeer : IDisposable
{
    /// <summary>
    /// Runs one offer/answer round: applies the description of the SFU, puts what we send into the
    /// sections we send in, answers, and gives each of those sections the SSRC the SFU expects there.
    /// Ends in <see cref="IVoicePeerListener.OnNegotiated"/> or <see cref="IVoicePeerListener.OnFailed"/>.
    /// </summary>
    void Negotiate(string offer, IReadOnlyList<SendSection> sends);

    /// <summary>
    /// A screen to send, whose pictures come encoded: it goes into a section through
    /// <see cref="Negotiate"/>.
    /// </summary>
    /// <param name="width">The size libwebrtc takes the stream for; the pictures keep their own.</param>
    /// <param name="maxFramerate">The most frames a second libwebrtc lets through.</param>
    /// <returns>Null where the platform sends no screens.</returns>
    IVideoSendSource? CreateVideoSource(int width, int height, int maxFramerate, IVideoSendListener listener) => null;

    /// <summary>
    /// A sound to send besides the microphone, as the one of a shown screen: it goes into a section
    /// through <see cref="Negotiate"/>.
    /// </summary>
    /// <returns>Null where the platform sends no such sound.</returns>
    IAudioSendSource? CreateAudioSource() => null;

    /// <summary>Stops or resumes sending the microphone, without touching its section.</summary>
    void SetMicrophoneEnabled(bool isEnabled);

    /// <summary>
    /// What the microphone is captured from and how it is processed. A peer that cannot choose,
    /// such as one whose system does it, keeps what it has.
    /// </summary>
    /// <returns>False when the input is not one the peer can capture.</returns>
    bool SetMicrophone(AudioInput input, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled,
        bool isAutoGainControlEnabled);

    /// <summary>The gain of the microphone, 1 keeps it as it is.</summary>
    void SetMicrophoneVolume(double volume);

    /// <summary>The voice gate of the microphone, or null to send it all the time.</summary>
    void SetVoiceGate(VoiceGateOptions? voiceGate);

    /// <summary>The playback volume of the voice of a section, 0 silences it.</summary>
    void SetVolume(string mid, double volume);

    /// <summary>Reads the statistics once; see <see cref="IVoicePeerListener.OnStats"/>.</summary>
    void RequestStats();
}

public interface IVoicePeerListener
{
    void OnConnectionChanged(WebRTCConnectionState state);

    /// <summary>The answer of a round, applied as the local description.</summary>
    void OnNegotiated(string localSdp);

    void OnFailed(string error);

    void OnStats(VoiceStats stats);

    /// <summary>
    /// A picture of the screen of a section, from a peer that decodes the screens itself, on the
    /// thread of its stream. It is only valid during the call.
    /// </summary>
    void OnVideoFrame(string mid, RawVideoFrame frame)
    {
    }
}
