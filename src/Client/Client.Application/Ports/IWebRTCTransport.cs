using Client.Core.Settings;
using Shared.Enums.WebRTC;
using Shared.Models.Media;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;

namespace Client.Application.Ports;

/// <summary>
/// WebRTC transport of one hybrid session: ICE, DTLS-SRTP and the media tracks sent and received
/// through the SFU.
/// </summary>
public interface IWebRTCTransport : IDisposable
{
    /// <summary>
    /// Raised once the local description is set. The parameters have to be delivered to the SFU
    /// with transport.connect before the DTLS handshake can succeed.
    /// </summary>
    event Action<DtlsParameters>? LocalDtlsParametersReady;

    event Action<WebRTCConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// Raised with a user id when the user starts or stops speaking, by the audio of their inbound
    /// tracks, or of our outbound ones for ourselves. Everyone is reported silent when the
    /// transport disconnects. Raised on a timer thread.
    /// </summary>
    event Action<int, bool>? SpeakingChanged;

    /// <summary>
    /// Raised with the user and the source of a video track for every frame of it: decoded frames
    /// of the inbound tracks, and the preview of our own outbound ones. Raised on a thread of the
    /// track, and the pixels are only valid during the call.
    /// </summary>
    event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady;

    /// <summary>
    /// Raised with the ids of the inbound tracks the transport can receive from now on: their
    /// sections went through a negotiation round. The SFU must not send a track before that, since
    /// packets of a stream the transport does not know yet are attached to the wrong section, or
    /// to none, for good. Raised once per track, on a thread pool thread.
    /// </summary>
    event Action<IReadOnlyList<string>>? InboundsReady;

    /// <summary>
    /// Raised with how the media fared since the previous reading of the statistics, every few
    /// seconds while the transport runs. Raised on a thread of webrtcbin.
    /// </summary>
    event Action<TransportStatsSample>? StatsSampled;

    WebRTCConnectionState ConnectionState { get; }

    /// <summary>
    /// Starts the negotiation against the given transport. The media sections come from the
    /// inbound tracks registered so far.
    /// </summary>
    bool Connect(TransportParameters transportParameters);

    void Disconnect();
    
    /// <param name="volume">
    /// Playback volume of the user's tracks of the same source, set before the track can play. It
    /// applies to the earlier tracks of that source as well; the voice and the sound of a screen
    /// keep volumes of their own.
    /// </param>
    void RegisterInbound(InboundTrack inbound, double volume);

    void UnregisterInbound(int userId, MediaType mediaType, MediaSourceType mediaSourceType);

    void UnregisterAllUserInbounds(int userId);

    /// <summary>
    /// Changes the playback volume of the audio tracks of a user from one source: the microphone,
    /// or the screen they show. A user without inbound tracks is skipped, the volume of the next
    /// track comes with <see cref="RegisterInbound"/>.
    /// </summary>
    /// <param name="volume">Linear gain, 0 silences the source.</param>
    void SetInboundVolume(int userId, MediaSourceType sourceType, double volume);

    /// <summary>
    /// Silences the voices of all users, the ones whose tracks come later included, or brings them
    /// back at the volume of each. The sound of a shown screen is not touched.
    /// </summary>
    void SetInboundVoiceMuted(bool isMuted);

    /// <summary>
    /// Starts sending an audio track from <paramref name="input"/> to a producer the SFU created for it.
    /// </summary>
    /// <param name="volume">Gain applied before encoding.</param>
    /// <param name="voiceGate">
    /// How the voice gate of a microphone decides, or null to send it all the time. Other inputs are
    /// never gated.
    /// </param>
    /// <param name="noiseSuppression">
    /// How much background noise is taken out of a microphone. Other inputs are sent as they are.
    /// </param>
    /// <param name="isEchoCancellationEnabled">
    /// Whether the voices played on the speakers are taken out of a microphone.
    /// </param>
    /// <param name="isAutoGainControlEnabled">
    /// Whether a quiet microphone is brought up to the level of speech. Other inputs are sent as they are.
    /// </param>
    bool StartOutbound(OutboundTrack outbound, AudioInput input, double volume, VoiceGateOptions? voiceGate,
        NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled, bool isAutoGainControlEnabled);

    /// <summary>
    /// Starts sending a video track captured from <paramref name="input"/> to a producer the SFU created for it.
    /// </summary>
    /// <param name="settings">The size and the frame rate the picture is sent with.</param>
    bool StartOutbound(OutboundTrack outbound, VideoInput input, ScreenShareSettings settings);

    /// <summary>
    /// Changes the gain of a running outbound track.
    /// </summary>
    void SetOutboundVolume(string outboundId, double volume);

    /// <summary>
    /// Makes a running audio track capture from another input, or with another noise suppression,
    /// echo cancelling or gain control, without touching its section or its producer. A failure is only logged, and
    /// the track then sends nothing until the next change.
    /// </summary>
    void SetOutboundInput(string outboundId, AudioInput input, NoiseSuppressionLevel noiseSuppression,
        bool isEchoCancellationEnabled, bool isAutoGainControlEnabled);

    /// <summary>
    /// Changes the voice gate of a running microphone track, null sends it all the time.
    /// </summary>
    void SetVoiceGate(string outboundId, VoiceGateOptions? voiceGate);

    /// <summary>
    /// Stops sending a track. Its section stays as inactive.
    /// </summary>
    void StopOutbound(string outboundId);

    /// <summary>
    /// Stops or resumes sending an outbound track without touching its section or its producer.
    /// </summary>
    bool SetOutboundMuted(string outboundId, bool isMuted);

    Task<bool> WaitUntilConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
