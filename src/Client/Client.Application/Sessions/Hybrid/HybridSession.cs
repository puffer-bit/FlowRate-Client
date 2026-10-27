using Client.Core.Settings;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Models.Media;
using System.Collections.Concurrent;
using Client.Core.Media;
using Client.Core.Coordinator;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Application.Sessions.Hybrid;

public partial class HybridSession : IHybridSession
{
    public HybridSessionModel Model { get; private set; }

    public HybridSessionState State
    {
        get;
        private set
        {
            // The transport and the reconnect loop both report a connection, from threads of their
            // own. A state raised twice would make the owner start the voice and the screen twice.
            lock (_stateLock)
            {
                if (_isStateSet && field == value)
                    return;

                _isStateSet = true;
                field = value;
            }

            StateChanged?.Invoke(value);
            LogStateChanged(Model.Id, value);
        }
    }

    private readonly Lock _stateLock = new();
    private bool _isStateSet;

    public event Action<HybridSessionState>? StateChanged;

    public event Action<HybridSessionError>? ErrorOccurred;

    private bool IsDisposed { get; set; }

    private readonly IWebRTCTransportFactory _transportFactory;
    private readonly bool _isPlaybackEnabled;
    private IWebRTCTransport _webRTCService;

    /// <summary>
    /// Identifies the DTLS context of this session for the SFU. It changes whenever the local
    /// transport is rebuilt, which is what tells the SFU to recreate its own side.
    /// </summary>
    public string ClientEpoch { get; private set; } = null!;

    private readonly ICoordinatorSession _coordinatorSession;
    private readonly IPeerPlaybackService? _peerPlayback;
    private readonly IVoiceActivityService? _voiceActivity;
    private readonly IScreenWatchService? _screenWatch;

    // Who the transport hears speaking, muted locally or not; the indicator leaves out the muted ones
    private readonly HashSet<int> _speakingUsers = [];
    private readonly Lock _speakingLock = new();

    // Kept here and not in the transport, which is replaced on a reconnect
    private volatile bool _isSoundMuted;
    private readonly ILogger<HybridSession> _logger;

    // The producer our microphone is sent to, or null while the microphone is off
    private OutboundTrack? _voiceOutbound;

    // The producer our screen is sent to, or null while it is not shown
    private OutboundTrack? _screenOutbound;

    // The producer the sound of our screen is sent to, or null while it is not shown or shown silent
    private OutboundTrack? _screenAudioOutbound;

    public event Action<int, bool>? ScreenShareChanged;

    // Users whose screen we watch, ourselves included; read for every frame on the threads of the tracks
    // The screens we watch, each with whether it comes with its sound
    private readonly ConcurrentDictionary<int, bool> _watchedScreens = new();

    public event Action<int, bool>? ScreenWatchChanged;

    public event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady;

    public event Action<TransportStatsSample>? StatsSampled;

    private static readonly TimeSpan ReconnectConnectTimeout = TimeSpan.FromSeconds(15);

    private const int ResumeInboundsAttempts = 3;
    private static readonly TimeSpan ResumeInboundsRetryDelay = TimeSpan.FromSeconds(1);

    private readonly Lock _reconnectLock = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task _reconnectTask = Task.CompletedTask;

    private bool IsReconnecting => !_reconnectTask.IsCompleted;

    // Set from the notifications of the server about the SFU node of the session
    private volatile bool _isWaitingForNode;
    private volatile bool _isRejoinRequired;

    // Completed when the node of the session comes back or is replaced, so the reconnect loop does not sit out its pause
    private TaskCompletionSource _reconnectWake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HybridSession(
        HybridSessionModel sessionModel,
        ICoordinatorSession coordinatorSession,
        IWebRTCTransportFactory transportFactory,
        ILogger<HybridSession> logger,
        bool isPlaybackEnabled = true,
        IPeerPlaybackService? peerPlayback = null,
        IVoiceActivityService? voiceActivity = null,
        IScreenWatchService? screenWatch = null)
    {
        Model = sessionModel;
        _coordinatorSession = coordinatorSession;
        _transportFactory = transportFactory;
        _logger = logger;
        _isPlaybackEnabled = isPlaybackEnabled;
        _peerPlayback = peerPlayback;
        _voiceActivity = voiceActivity;
        _screenWatch = screenWatch;
        _webRTCService = CreateTransport();

        _coordinatorSession.HybridSessionUpdated += HandleHybridSessionEvent;
        _coordinatorSession.HybridSessionNodeChanged += HandleNodeEvent;

        if (_peerPlayback != null)
            _peerPlayback.Changed += OnPeerPlaybackChanged;

        State = HybridSessionState.WaitingForConnection;
    }

    public void Connect(TransportParameters transportParameters)
    {
        if (_webRTCService.Connect(transportParameters))
            return;

        State = HybridSessionState.Failed;
        ErrorOccurred?.Invoke(new HybridSessionError(HybridSessionErrorKind.TransportStartFailed));
    }

    /// <summary>
    /// Every transport gets an epoch of its own: webrtcbin creates a new certificate with it, and
    /// transport.connect of the SFU accepts a fingerprint only once.
    /// </summary>
    private IWebRTCTransport CreateTransport()
    {
        var transport = _transportFactory.Create(Model.Id, _isPlaybackEnabled);

        // The sender is captured, so that late notifications of a replaced transport are recognized
        transport.ConnectionStateChanged += state => HandleConnectionStatus(transport, state);
        transport.LocalDtlsParametersReady += parameters => SendLocalDtlsParameters(transport, parameters);
        transport.InboundsReady += inboundIds => ResumeInbounds(transport, inboundIds);
        transport.SpeakingChanged += (userId, isSpeaking) => OnSpeakingChanged(transport, userId, isSpeaking);
        transport.StatsSampled += sample =>
        {
            if (transport == _webRTCService && !IsDisposed)
                StatsSampled?.Invoke(sample);
        };
        transport.VideoFrameReady += (userId, source, frame) =>
        {
            if (transport != _webRTCService || IsDisposed)
                return;

            // A paused screen still delivers the frames that were on their way, and our own
            // preview runs whether it is watched or not
            if (source == MediaSourceType.Screen && !_watchedScreens.ContainsKey(userId))
                return;

            VideoFrameReady?.Invoke(userId, source, frame);
        };

        // A new transport plays everything, while the user may have silenced the voices
        transport.SetInboundVoiceMuted(_isSoundMuted);

        ClientEpoch = Guid.NewGuid().ToString("N");

        return transport;
    }

    /// <summary>
    /// Hands our certificate fingerprint to the SFU. The transport stays unusable until this
    /// reaches the server, because the SFU verifies the peer certificate against it.
    /// </summary>
    private async void SendLocalDtlsParameters(IWebRTCTransport transport, DtlsParameters dtlsParameters)
    {
        try
        {
            var result = await _coordinatorSession.ConnectHybridSessionTransport(Model.Id, dtlsParameters);

            if (transport != _webRTCService)
                return;

            if (result == HybridSessionConnectTransportResult.NoError)
            {
                LogDtlsAccepted(Model.Id);
                return;
            }

            // A rejection is reported to the user by the subscriber of RequestRejected
            if (result != HybridSessionConnectTransportResult.Rejected)
                ErrorOccurred?.Invoke(new HybridSessionError(HybridSessionErrorKind.DtlsParametersRejected, result.ToString()));

            ScheduleReconnect($"the SFU rejected the DTLS parameters: {result}");
        }
        catch (Exception exception)
        {
            if (transport != _webRTCService)
                return;

            ErrorOccurred?.Invoke(new HybridSessionError(HybridSessionErrorKind.DtlsParametersSendFailed, exception.Message));
            ScheduleReconnect($"failed to send the DTLS parameters: {exception.Message}");
        }
    }

    /// <summary>
    /// Lets the SFU start the tracks the transport is ready for. A track stays silent until this
    /// reaches the server, so a request that got no answer is repeated.
    /// </summary>
    private async void ResumeInbounds(IWebRTCTransport transport, IReadOnlyList<string> inboundIds)
    {
        try
        {
            for (var attempt = 1; attempt <= ResumeInboundsAttempts; attempt++)
            {
                // The tracks of a replaced transport are gone on the SFU as well
                if (transport != _webRTCService || IsDisposed)
                    return;

                var result = await _coordinatorSession.ResumeInboundsInHybridSession(Model.Id, inboundIds);

                if (result == ResumeInboundsResult.NoError)
                    return;

                LogResumeInboundsFailed(Model.Id, result, attempt);

                if (result is not (ResumeInboundsResult.TimedOut or ResumeInboundsResult.SFUError
                    or ResumeInboundsResult.InternalError))
                    return;

                await Task.Delay(ResumeInboundsRetryDelay, _lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            // Raised from an event of the transport, where nobody would see the failure
            LogResumeInboundsStopped(exception, Model.Id);
        }
    }

    public Task<StartVoiceShareResult> StartVoiceShareAsync(string? inputDeviceId, double inputVolume,
        VoiceGateOptions? voiceGate, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled,
        bool isAutoGainControlEnabled, bool isMuted = false) =>
        StartVoiceShareAsync(new DeviceAudioInput(inputDeviceId), inputVolume, voiceGate, noiseSuppression,
            isEchoCancellationEnabled, isAutoGainControlEnabled, isMuted);

    /// <summary>
    /// Asks the SFU for a voice producer and starts sending <paramref name="input"/> to it. Besides the
    /// microphone this takes a looped file or a tone, which is what the test bot sends.
    /// </summary>
    public async Task<StartVoiceShareResult> StartVoiceShareAsync(AudioInput input, double inputVolume,
        VoiceGateOptions? voiceGate = null, NoiseSuppressionLevel noiseSuppression = NoiseSuppressionLevel.Moderate,
        bool isEchoCancellationEnabled = false, bool isAutoGainControlEnabled = true, bool isMuted = false)
    {
        if (_voiceOutbound != null)
            return StartVoiceShareResult.NoError;

        var transport = _webRTCService;
        var (result, outbound) = await _coordinatorSession.StartVoiceShareInHybridSession(Model.Id);

        if (result != StartVoiceShareResult.NoError)
            return result;

        if (outbound == null)
            return StartVoiceShareResult.InternalError;

        // The transport was rebuilt in the meantime, and the rejoin released the producer with it
        if (transport != _webRTCService)
            return StartVoiceShareResult.InternalError;

        if (!transport.StartOutbound(outbound, input, inputVolume, voiceGate, noiseSuppression,
                isEchoCancellationEnabled, isAutoGainControlEnabled))
        {
            LogStartSendingFailed(Model.Id, input);
            await _coordinatorSession.StopVoiceShareInHybridSession(Model.Id);
            return StartVoiceShareResult.InternalError;
        }

        _voiceOutbound = outbound;
        IsMicrophoneMuted = false;
        LogVoiceStarted(Model.Id, outbound.SSRC);

        // Silenced here whatever the server answers, the others only see it later in that case
        if (isMuted)
            await SetMicrophoneMutedAsync(true);

        return StartVoiceShareResult.NoError;
    }

    public async Task<StopVoiceShareResult> StopVoiceShareAsync()
    {
        var outbound = _voiceOutbound;

        if (outbound == null)
            return StopVoiceShareResult.NoError;

        _voiceOutbound = null;
        IsMicrophoneMuted = false;
        _webRTCService.StopOutbound(outbound.Id);
        LogVoiceStopped(Model.Id);

        return await _coordinatorSession.StopVoiceShareInHybridSession(Model.Id);
    }

    public bool IsVoiceStarted => _voiceOutbound != null;

    public bool IsScreenShareStarted => _screenOutbound != null;

    public async Task<StartScreenShareResult> StartScreenShareAsync(VideoInput input, ScreenShareSettings settings,
        AudioInput? audioInput = null)
    {
        if (_screenOutbound != null)
            return StartScreenShareResult.NoError;

        var transport = _webRTCService;
        var (result, outbounds) = await _coordinatorSession.StartScreenShareInHybridSession(Model.Id, audioInput != null);

        if (result != StartScreenShareResult.NoError)
            return result;

        if (outbounds == null)
            return StartScreenShareResult.InternalError;

        var outbound = outbounds.Video;

        // The transport was rebuilt in the meantime, and the rejoin released the producer with it
        if (transport != _webRTCService)
            return StartScreenShareResult.InternalError;

        if (!transport.StartOutbound(outbound, input, settings))
        {
            LogScreenShareStartFailed(Model.Id, input);
            await _coordinatorSession.StopScreenShareInHybridSession(Model.Id);
            return StartScreenShareResult.UnsupportedMedia;
        }

        _screenOutbound = outbound;
        LogScreenShareStarted(Model.Id, outbound.SSRC);

        // The picture goes out without the sound rather than not at all
        if (outbounds.Audio is { } audioOutbound && audioInput != null)
            StartScreenAudio(transport, audioOutbound, audioInput);

        ScreenShareChanged?.Invoke(outbound.UserId, true);

        return StartScreenShareResult.NoError;
    }

    private void StartScreenAudio(IWebRTCTransport transport, OutboundTrack audioOutbound, AudioInput audioInput)
    {
        if (transport.StartOutbound(audioOutbound, audioInput, 1.0, null, NoiseSuppressionLevel.Off, false, false))
        {
            _screenAudioOutbound = audioOutbound;
            LogScreenAudioStarted(Model.Id, audioOutbound.SSRC);
        }
        else
            LogScreenAudioStartFailed(Model.Id, audioInput);
    }

    /// <summary>
    /// Gives a running share the sound it was started without. The server adds the producer to the
    /// share and hands it to those who watch, so the picture goes on meanwhile.
    /// </summary>
    private async Task AddScreenAudioAsync(OutboundTrack outbound, AudioInput audioInput)
    {
        var transport = _webRTCService;
        var (result, outbounds) = await _coordinatorSession.StartScreenShareInHybridSession(Model.Id, true);

        // Stopped, or moved to a new transport that starts it anew, in the meantime
        if (_screenOutbound != outbound || transport != _webRTCService || _screenAudioOutbound != null)
            return;

        // A server that cannot add it leaves the share silent rather than restarting it under its viewers
        if (result != StartScreenShareResult.NoError || outbounds?.Audio is not { } audioOutbound)
        {
            LogScreenAudioStartFailed(Model.Id, audioInput);
            return;
        }

        StartScreenAudio(transport, audioOutbound, audioInput);
    }

    public async Task<StartScreenShareResult> ChangeScreenShareAsync(VideoInput input, ScreenShareSettings settings,
        AudioInput? audioInput = null)
    {
        var outbound = _screenOutbound;
        var audioOutbound = _screenAudioOutbound;

        if (outbound == null)
            return await StartScreenShareAsync(input, settings, audioInput);

        var transport = _webRTCService;

        // The branch is built anew on the same producer, so the viewers keep receiving it
        transport.StopOutbound(outbound.Id);

        if (!transport.StartOutbound(outbound, input, settings))
        {
            LogScreenShareStartFailed(Model.Id, input);
            await StopScreenShareAsync();
            return StartScreenShareResult.UnsupportedMedia;
        }

        LogScreenShareChanged(Model.Id, outbound.SSRC);

        // A sound that is dropped stays muted on its producer until the share ends
        if (audioOutbound != null)
        {
            if (audioInput != null)
                transport.SetOutboundInput(audioOutbound.Id, audioInput, NoiseSuppressionLevel.Off, false, false);

            transport.SetOutboundMuted(audioOutbound.Id, audioInput == null);
        }
        else if (audioInput != null)
            await AddScreenAudioAsync(outbound, audioInput);

        return StartScreenShareResult.NoError;
    }

    public async Task<StopScreenShareResult> StopScreenShareAsync()
    {
        var outbound = _screenOutbound;

        if (outbound == null)
            return StopScreenShareResult.NoError;

        _screenOutbound = null;
        _webRTCService.StopOutbound(outbound.Id);

        if (Interlocked.Exchange(ref _screenAudioOutbound, null) is { } audioOutbound)
            _webRTCService.StopOutbound(audioOutbound.Id);

        LogScreenShareStopped(Model.Id);
        SetScreenWatched(outbound.UserId, false);
        ScreenShareChanged?.Invoke(outbound.UserId, false);

        return await _coordinatorSession.StopScreenShareInHybridSession(Model.Id);
    }

    public bool IsWatchingScreen(int userId) => _watchedScreens.ContainsKey(userId);

    public bool HasWatchedScreenAudio(int userId) => _watchedScreens.TryGetValue(userId, out var hasAudio) && hasAudio;

    public Task<SetScreenShareWatchResult> SetScreenWatchAsync(int userId, bool isWatching)
    {
        if (userId != _coordinatorSession.GetCurrentUser().Id)
            return EnqueuePeerWork(() => SetPeerScreenWatchAsync(userId, isWatching));

        // Our own screen is not received from the SFU, the frames come from the branch that sends it
        if (isWatching && _screenOutbound == null)
            return Task.FromResult(SetScreenShareWatchResult.ScreenShareNotActive);

        SetScreenWatched(userId, isWatching);

        return Task.FromResult(SetScreenShareWatchResult.NoError);
    }

    /// <summary>
    /// Runs in the chain of the peer events, so that a stop of the same screen cannot be handled
    /// while the answer of the server is on its way.
    /// </summary>
    private async Task<SetScreenShareWatchResult> SetPeerScreenWatchAsync(int userId, bool isWatching)
    {
        if (IsDisposed)
            return SetScreenShareWatchResult.InternalError;

        if (isWatching == _watchedScreens.ContainsKey(userId))
            return SetScreenShareWatchResult.NoError;

        var transport = _webRTCService;
        var (result, inbounds) = await _coordinatorSession.SetScreenShareWatchInHybridSession(Model.Id, userId, isWatching);

        if (!isWatching)
        {
            // Hidden whatever the server said: the user asked not to see it
            SetScreenWatched(userId, false);
            LogScreenWatchChanged(Model.Id, userId, false);

            return result;
        }

        if (result != SetScreenShareWatchResult.NoError)
            return result;

        // The transport was rebuilt in the meantime, and the rejoin released the consumer with it
        if (inbounds == null || transport != _webRTCService)
            return SetScreenShareWatchResult.InternalError;

        // A track the transport already knows is left alone, which is the case when a paused
        // screen is watched again
        var volume = await GetPeerVolumeAsync(userId, PeerAudioChannel.Screen);
        transport.RegisterInbound(inbounds.Video, volume);

        if (inbounds.Audio != null)
            transport.RegisterInbound(inbounds.Audio, volume);

        SetScreenWatched(userId, true, inbounds.Audio != null);
        LogScreenWatchChanged(Model.Id, userId, true);

        return SetScreenShareWatchResult.NoError;
    }

    private void SetScreenWatched(int userId, bool isWatched, bool hasAudio = false)
    {
        var isChanged = isWatched ? _watchedScreens.TryAdd(userId, hasAudio) : _watchedScreens.TryRemove(userId, out _);

        if (!isChanged)
            return;

        _screenWatch?.Set(Model.Id, userId, isWatched);
        ScreenWatchChanged?.Invoke(userId, isWatched);
    }

    /// <summary>
    /// Forgets what is watched, when the transport that received it goes away.
    /// </summary>
    private void ClearWatchedScreens()
    {
        foreach (var userId in _watchedScreens.Keys)
            SetScreenWatched(userId, false);
    }

    public bool IsMicrophoneMuted { get; private set; }

    public async Task<SetMicrophoneMuteResult> SetMicrophoneMutedAsync(bool isMuted)
    {
        var outbound = _voiceOutbound;

        if (outbound == null)
            return SetMicrophoneMuteResult.VoiceNotStarted;

        if (isMuted)
        {
            // Silenced locally first: the microphone must go quiet at once, even if the server is slow
            _webRTCService.SetOutboundMuted(outbound.Id, true);
            IsMicrophoneMuted = true;

            return await _coordinatorSession.SetMicrophoneMuteInHybridSession(Model.Id, true);
        }

        // Resumed on the server first, so the first words after unmuting are not dropped by a paused producer
        var result = await _coordinatorSession.SetMicrophoneMuteInHybridSession(Model.Id, false);

        if (result != SetMicrophoneMuteResult.NoError)
            return result;

        _webRTCService.SetOutboundMuted(outbound.Id, false);
        IsMicrophoneMuted = false;

        return SetMicrophoneMuteResult.NoError;
    }

    public bool IsSoundMuted => _isSoundMuted;

    public bool IsAfk { get; private set; }
    public string? AfkReason { get; private set; }

    public async Task<SetAfkResult> SetAfkAsync(bool isAfk, string? reason)
    {
        IsAfk = isAfk;
        AfkReason = isAfk && !string.IsNullOrWhiteSpace(reason) ? reason.Trim() : null;

        return await _coordinatorSession.SetAfkInHybridSession(Model.Id, isAfk, AfkReason);
    }

    public Task<PokeResult> PokeAsync(int userId, string? message) =>
        _coordinatorSession.PokeInHybridSession(Model.Id, userId,
            string.IsNullOrWhiteSpace(message) ? null : message.Trim());

    public async Task<SetSoundMuteResult> SetSoundMutedAsync(bool isMuted)
    {
        _isSoundMuted = isMuted;
        _webRTCService.SetInboundVoiceMuted(isMuted);

        lock (_speakingLock)
        {
            foreach (var userId in _speakingUsers)
                PublishSpeaking(userId);
        }

        return await _coordinatorSession.SetSoundMuteInHybridSession(Model.Id, isMuted);
    }

    private void OnSpeakingChanged(IWebRTCTransport transport, int userId, bool isSpeaking)
    {
        if (transport != _webRTCService || IsDisposed)
            return;

        lock (_speakingLock)
        {
            if (isSpeaking)
                _speakingUsers.Add(userId);
            else
                _speakingUsers.Remove(userId);

            PublishSpeaking(userId);
        }
    }

    /// <summary>
    /// Shows a user as speaking unless we muted them or all the voices: a muted user is not heard,
    /// so the indicator would point at nothing. Called under <see cref="_speakingLock"/>.
    /// </summary>
    private void PublishSpeaking(int userId)
    {
        var isMuted = _isSoundMuted || _peerPlayback?.Get(userId, PeerAudioChannel.Voice).IsMuted == true;

        _voiceActivity?.Set(Model.Id, userId, _speakingUsers.Contains(userId) && !isMuted);
    }

    /// <summary>
    /// Forgets who speaks, when the transport that heard it goes away.
    /// </summary>
    private void ClearSpeaking()
    {
        lock (_speakingLock)
        {
            _speakingUsers.Clear();
            _voiceActivity?.Clear(Model.Id);
        }
    }

    public void SetInputVolume(double volume)
    {
        if (_voiceOutbound is { } outbound)
            _webRTCService.SetOutboundVolume(outbound.Id, volume);
    }

    public void SetMicrophoneInput(string? inputDeviceId, NoiseSuppressionLevel noiseSuppression,
        bool isEchoCancellationEnabled, bool isAutoGainControlEnabled)
    {
        if (_voiceOutbound is { } outbound)
            _webRTCService.SetOutboundInput(outbound.Id, new DeviceAudioInput(inputDeviceId), noiseSuppression,
                isEchoCancellationEnabled, isAutoGainControlEnabled);
    }

    public void SetVoiceGate(VoiceGateOptions? voiceGate)
    {
        if (_voiceOutbound is { } outbound)
            _webRTCService.SetVoiceGate(outbound.Id, voiceGate);
    }

    private void HandleConnectionStatus(IWebRTCTransport transport, WebRTCConnectionState state)
    {
        if (IsDisposed || transport != _webRTCService)
            return;

        if (state == WebRTCConnectionState.Connected)
        {
            State = HybridSessionState.Connected;
            return;
        }

        // While reconnecting the loop owns the state, the attempts only report their success
        if (IsReconnecting)
            return;

        switch (state)
        {
            case WebRTCConnectionState.Connecting:
                State = HybridSessionState.WaitingForNegotiation;
                break;
            // ICE often gets over a disconnect by itself, so the first attempt waits for it
            case WebRTCConnectionState.Disconnected:
                ScheduleReconnect($"peer connection state {state}", waitForRecovery: true);
                break;
            case WebRTCConnectionState.Failed:
            case WebRTCConnectionState.Fatal:
            case WebRTCConnectionState.Closed:
                ScheduleReconnect($"peer connection state {state}");
                break;
        }
    }

    /// <summary>
    /// Starts the reconnect loop unless it already runs. Does nothing once the session is closed.
    /// </summary>
    /// <param name="waitForRecovery">
    /// The first attempt waits its pause, for a transport that may come back on its own; otherwise
    /// nothing is left to wait for and it starts at once.
    /// </param>
    private void ScheduleReconnect(string reason, bool waitForRecovery = false)
    {
        lock (_reconnectLock)
        {
            if (IsDisposed || IsReconnecting)
                return;

            LogTransportLost(Model.Id, reason);

            var cancellationToken = _lifetime.Token;
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(waitForRecovery, cancellationToken), CancellationToken.None);
        }
    }

    private async Task ReconnectLoopAsync(bool waitForRecovery, CancellationToken cancellationToken)
    {
        State = _isWaitingForNode ? HybridSessionState.WaitingForSfuNode : HybridSessionState.Reconnecting;

        try
        {
            var wake = _reconnectWake.Task;

            for (var attempt = 1; ; attempt++)
            {
                if (_isWaitingForNode)
                {
                    // The old node may still carry our media, while the server already dropped us from it
                    if (_webRTCService.ConnectionState != WebRTCConnectionState.New)
                        ReplaceTransport(cancellationToken);

                    LogWaitingForNode(Model.Id);
                    State = HybridSessionState.WaitingForSfuNode;

                    // Nothing is worth trying without a node, the server tells us when there is one
                    await PauseAsync(ReconnectBackoff.LongestDelay, wake, cancellationToken);
                    wake = _reconnectWake.Task;

                    // A node that came back starts the attempts over, the first one at once
                    attempt = 0;
                    waitForRecovery = false;
                    continue;
                }

                // Every second of the pause is a second of silence
                var delay = attempt == 1 && !waitForRecovery ? TimeSpan.Zero : ReconnectBackoff.GetDelay(attempt);
                await PauseAsync(delay, wake, cancellationToken);

                // Taken before the attempt, so that a node coming back during it cuts the next pause short
                wake = _reconnectWake.Task;

                if (_isWaitingForNode)
                    continue;

                State = HybridSessionState.Reconnecting;

                // A disconnected transport may come back on its own during the first pause, unless
                // the server dropped what the node held for us
                if (!_isRejoinRequired && _webRTCService.ConnectionState == WebRTCConnectionState.Connected)
                {
                    LogTransportRecovered(Model.Id);
                    State = HybridSessionState.Connected;
                    return;
                }

                // A lost coordinator closes the opened sessions, but its own reconnect may still bring it back
                if (_coordinatorSession.ConnectionStatus != CoordinatorState.Connected)
                {
                    LogReconnectAttemptSkipped(Model.Id, attempt);
                    continue;
                }

                LogReconnectAttemptStarted(Model.Id, attempt);

                var (isConnected, fatalResult) = await RejoinAsync(cancellationToken);

                if (isConnected)
                {
                    _isRejoinRequired = false;
                    LogReconnected(Model.Id, attempt);
                    State = HybridSessionState.Connected;
                    return;
                }

                if (fatalResult != null)
                {
                    // A rejection is reported to the user by the subscriber of RequestRejected
                    if (fatalResult != HybridSessionJoinResult.Rejected)
                        ErrorOccurred?.Invoke(new HybridSessionError(HybridSessionErrorKind.RejoinFailed, fatalResult.ToString()));

                    State = HybridSessionState.Failed;
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogReconnectCanceled(Model.Id);
        }
        catch (Exception exception)
        {
            // Nobody awaits the loop, so a failure would go unnoticed
            LogReconnectStopped(exception, Model.Id);
            State = HybridSessionState.Failed;
        }
    }

    /// <summary>
    /// Waits for <paramref name="delay"/>, or less if <paramref name="wake"/> completes first.
    /// </summary>
    private static async Task PauseAsync(TimeSpan delay, Task wake, CancellationToken cancellationToken)
    {
        using var pause = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await Task.WhenAny(Task.Delay(delay, pause.Token), wake);
        pause.Cancel();

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Lets the reconnect loop try again right away instead of finishing its pause.
    /// </summary>
    private void WakeReconnect() =>
        Interlocked.Exchange(ref _reconnectWake, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult();

    /// <summary>
    /// Replaces the transport and joins again with its new epoch. The server releases our producers
    /// and consumers on a rejoin and creates the consumers again once the new transport connects.
    /// </summary>
    /// <returns>Whether the new transport connected, or the join result that retrying cannot fix.</returns>
    private async Task<(bool IsConnected, HybridSessionJoinResult? FatalResult)> RejoinAsync(
        CancellationToken cancellationToken)
    {
        var transport = ReplaceTransport(cancellationToken);

        HybridSessionJoinResult result;
        TransportParameters? parameters;

        try
        {
            (result, parameters) = await _coordinatorSession.JoinHybridSession(Model.Id, ClientEpoch);
        }
        catch (Exception exception)
        {
            LogRejoinFailed(Model.Id, exception.Message);
            return (false, null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (result != HybridSessionJoinResult.NoError || parameters == null)
        {
            LogRejoinRejected(Model.Id, result);

            return result is HybridSessionJoinResult.NoError or HybridSessionJoinResult.InternalError
                or HybridSessionJoinResult.TimedOut or HybridSessionJoinResult.SFUError
                or HybridSessionJoinResult.SFUNotActive
                ? (false, null)
                : (false, result);
        }

        if (!transport.Connect(parameters))
        {
            LogNewTransportFailed(Model.Id);
            return (false, null);
        }

        return (await transport.WaitUntilConnectedAsync(ReconnectConnectTimeout, cancellationToken), null);
    }

    private IWebRTCTransport ReplaceTransport(CancellationToken cancellationToken)
    {
        IWebRTCTransport previous;
        IWebRTCTransport transport;

        lock (_reconnectLock)
        {
            // Checked under the lock, so that a transport is never created after Dispose took the last one
            cancellationToken.ThrowIfCancellationRequested();

            previous = _webRTCService;
            transport = CreateTransport();
            _webRTCService = transport;
        }

        // The producer belonged to the old transport, the owner starts the voice again once connected
        _voiceOutbound = null;
        IsMicrophoneMuted = false;

        // Same for the screen: the owner shows it again once connected
        _screenAudioOutbound = null;

        if (Interlocked.Exchange(ref _screenOutbound, null) is { } screen)
            ScreenShareChanged?.Invoke(screen.UserId, false);

        // The consumers of the screens we watched are gone as well, the owner asks for them again
        ClearWatchedScreens();

        previous.Dispose();

        // The old transport reports its silence after it was replaced, which is ignored
        ClearSpeaking();

        return transport;
    }

    public void Dispose()
    {
        IWebRTCTransport transport;

        lock (_reconnectLock)
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            _lifetime.Cancel();
            transport = _webRTCService;
        }

        // Otherwise a closed session keeps handling notifications against a disposed transport
        _coordinatorSession.HybridSessionUpdated -= HandleHybridSessionEvent;
        _coordinatorSession.HybridSessionNodeChanged -= HandleNodeEvent;

        if (_peerPlayback != null)
            _peerPlayback.Changed -= OnPeerPlaybackChanged;

        transport.Dispose();
        ClearSpeaking();
        ClearWatchedScreens();
        LogClosed(Model.Id);
    }
}
