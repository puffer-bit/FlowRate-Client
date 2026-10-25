using Client.Core.Settings;
using Shared.Enums;
using Shared.Models.Media;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Application.Ports;

namespace Client.Application.Sessions.Hybrid;

public interface IHybridSession : IDisposable
{
    HybridSessionModel Model { get; }
    
    HybridSessionState State { get; }
    
    event Action<HybridSessionState> StateChanged;
    
    event Action<HybridSessionError>? ErrorOccurred;
    
    /// <summary>
    /// Identifies the DTLS context of this session, sent with the join request. It changes when the
    /// session rebuilds its transport after a failure and rejoins on its own.
    /// </summary>
    string ClientEpoch { get; }

    void Connect(TransportParameters transportParameters);
    
    /// <summary>
    /// Asks the SFU for a voice producer and starts sending the microphone to it.
    /// </summary>
    /// <param name="inputDeviceId">Capture device, or null for the system default one.</param>
    /// <param name="inputVolume">Gain applied to the microphone before encoding.</param>
    /// <param name="voiceGate">
    /// How the voice gate of the microphone decides, or null to send it all the time.
    /// </param>
    /// <param name="noiseSuppression">How much background noise is taken out of the microphone.</param>
    /// <param name="isMuted">Starts muted, so that nothing is heard before the mute is put back.</param>
    Task<StartVoiceShareResult> StartVoiceShareAsync(string? inputDeviceId, double inputVolume,
        VoiceGateOptions? voiceGate, NoiseSuppressionLevel noiseSuppression, bool isMuted = false);

    /// <summary>
    /// Same as the microphone overload, but sends any <paramref name="input"/>: a looped file or a tone as well.
    /// </summary>
    Task<StartVoiceShareResult> StartVoiceShareAsync(AudioInput input, double inputVolume,
        VoiceGateOptions? voiceGate = null, NoiseSuppressionLevel noiseSuppression = NoiseSuppressionLevel.Moderate,
        bool isMuted = false);

    Task<StopVoiceShareResult> StopVoiceShareAsync();

    /// <summary>
    /// Whether our microphone has a producer on the SFU, muted or not.
    /// </summary>
    bool IsVoiceStarted { get; }

    bool IsMicrophoneMuted { get; }

    /// <summary>
    /// Mutes or unmutes the microphone. The producer is paused on the SFU rather than closed, so
    /// unmuting is instant and the listeners keep their consumers.
    /// </summary>
    Task<SetMicrophoneMuteResult> SetMicrophoneMutedAsync(bool isMuted);

    /// <summary>
    /// Whether the voices of the session are silenced. Kept over a reconnect, unlike the server,
    /// which has to be told again.
    /// </summary>
    bool IsSoundMuted { get; }

    /// <summary>
    /// Silences or brings back the voices of everybody, and tells the server, which shows it to the
    /// others. The sound of a shown screen plays on. The voices change here whatever the server answers.
    /// </summary>
    Task<SetSoundMuteResult> SetSoundMutedAsync(bool isMuted);

    /// <summary>
    /// Asks the SFU for a screen producer and starts sending <paramref name="input"/> to it.
    /// </summary>
    /// <param name="settings">The size and the frame rate the picture is sent with.</param>
    /// <param name="audioInput">
    /// The sound sent along with the picture, or null to share it silent. A sound that cannot be
    /// captured is left out, and the picture is sent anyway.
    /// </param>
    Task<StartScreenShareResult> StartScreenShareAsync(VideoInput input, ScreenShareSettings settings,
        AudioInput? audioInput = null);

    /// <summary>
    /// Shows another source or with other settings on the producer of the running share, so that
    /// the viewers keep watching it. Starts a new share where there is none, or where a sound is
    /// added to a share that went out silent.
    /// </summary>
    /// <param name="audioInput">The sound sent along, or null to mute it.</param>
    Task<StartScreenShareResult> ChangeScreenShareAsync(VideoInput input, ScreenShareSettings settings,
        AudioInput? audioInput = null);

    Task<StopScreenShareResult> StopScreenShareAsync();

    /// <summary>
    /// Whether our screen has a producer on the SFU. A transport that is rebuilt takes it away.
    /// </summary>
    bool IsScreenShareStarted { get; }

    /// <summary>
    /// Raised with a user id when the user starts or stops showing a screen, ourselves included.
    /// Raised on any thread.
    /// </summary>
    event Action<int, bool>? ScreenShareChanged;

    /// <summary>
    /// Starts or stops watching the screen a user shows. Nobody's screen is received until it is
    /// asked for; a screen that is no longer watched is paused on the SFU, so watching it again is
    /// quick. Our own screen is watched as a local preview.
    /// </summary>
    /// <returns><see cref="SetScreenShareWatchResult.ScreenShareNotActive"/> if the user shows nothing.</returns>
    Task<SetScreenShareWatchResult> SetScreenWatchAsync(int userId, bool isWatching);

    bool IsWatchingScreen(int userId);

    /// <summary>
    /// Raised with a user id when we start or stop watching the screen of the user. Watching ends on
    /// its own when the user stops showing, leaves, or our transport is rebuilt. Raised on any thread.
    /// </summary>
    event Action<int, bool>? ScreenWatchChanged;

    /// <summary>
    /// Raised with the user and the source for every frame of a video track: the screens we watch,
    /// our own preview among them. Raised on a thread of the track, and the pixels are only valid
    /// during the call.
    /// </summary>
    event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady;

    /// <summary>
    /// Raised with how the media of the current transport fared, every few seconds while it runs.
    /// Raised on any thread.
    /// </summary>
    event Action<TransportStatsSample>? StatsSampled;

    /// <summary>
    /// Changes the gain of the running microphone. Without one nothing happens: the next start
    /// takes the volume it is given.
    /// </summary>
    void SetInputVolume(double volume);

    /// <summary>
    /// Switches the running microphone to another device, or to another noise suppression, without
    /// stopping the voice share. Without a running microphone nothing happens.
    /// </summary>
    /// <param name="inputDeviceId">Capture device, or null for the system default one.</param>
    void SetMicrophoneInput(string? inputDeviceId, NoiseSuppressionLevel noiseSuppression);

    /// <summary>
    /// Changes the voice gate of the running microphone, null turns the gate off. Without a running
    /// microphone nothing happens.
    /// </summary>
    void SetVoiceGate(VoiceGateOptions? voiceGate);
}