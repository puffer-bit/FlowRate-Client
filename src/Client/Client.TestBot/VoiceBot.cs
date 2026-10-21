using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Models.Media;
using Client.Core.Media;
using Client.Core.Coordinator;
using Client.Core.Sessions.Hybrid;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Sessions.Hybrid;

namespace Client.TestBot;

/// <summary>
/// One bot: a regular user that connects to the coordinator, joins a voice session through the
/// same requests the desktop client sends, and plays <see cref="AudioInput"/> into it.
/// </summary>
internal sealed partial class VoiceBot
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    private readonly int _index;
    private readonly BotOptions _options;
    private readonly AudioInput _input;
    private readonly IServiceProvider _services;
    private readonly IWebRTCTransportFactory _transports;
    private readonly ILogger<VoiceBot> _logger;
    private readonly LoadStats _stats;

    private ICoordinatorSession? _coordinator;
    private HybridSession? _session;
    private HybridSessionModel? _sessionModel;

    // Whether the screen should be on right now; a reconnect brings it back only then
    private bool _isScreenWanted;

    // Whether shared screens should be watched right now; toggled by --watch-interval
    private bool _isWatchWanted;

    // Users showing a screen, ourselves left out
    private readonly HashSet<int> _sharingUsers = [];
    private readonly Lock _sharingUsersLock = new();

    // Frames received per user since the last report; touched on the threads of the video tracks
    private readonly Dictionary<int, (int Count, int Width, int Height)> _receivedVideo = [];
    private readonly Lock _receivedVideoLock = new();

    // Users whose audio arrives right now, ourselves included; touched on a timer thread of the transport
    private readonly VoiceActivityService _voiceActivity = new();
    private readonly HashSet<int> _heardUsers = [];
    private readonly Lock _heardUsersLock = new();

    private string Name => $"Bot {_index + 1}";

    public VoiceBot(int index, BotOptions options, AudioInput input, IServiceProvider services,
        IWebRTCTransportFactory transports, LoadStats stats)
    {
        _stats = stats;
        _index = index;
        _options = options;
        _input = input;
        _services = services;
        _transports = transports;
        _logger = services.GetRequiredService<ILogger<VoiceBot>>();

        _voiceActivity.Changed += (_, userId, isSpeaking) =>
        {
            lock (_heardUsersLock)
            {
                if (isSpeaking)
                    _heardUsers.Add(userId);
                else
                    _heardUsers.Remove(userId);
            }
        };
    }

    /// <summary>
    /// Joins the session, starts the voice and keeps it running until <paramref name="cancellationToken"/> fires.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _coordinator = ActivatorUtilities.CreateInstance<CoordinatorSession>(_services);

        var identity = LoadIdentity();
        var connectStarted = Stopwatch.GetTimestamp();
        await _coordinator.Connect(_options.Host, _options.Port, identity, _options.Password);

        if (_coordinator is { ConnectionStatus: CoordinatorState.CertificateNotTrusted, UntrustedCertificate: { } untrusted })
        {
            Log($"trusting certificate {untrusted.Certificate.Fingerprint}");
            await _coordinator.TrustCertificateAsync(untrusted);
            await _coordinator.Connect(_options.Host, _options.Port, identity, _options.Password);
        }

        if (_coordinator.ConnectionStatus != CoordinatorState.Connected)
            throw new InvalidOperationException($"{Name}: failed to connect: {_coordinator.ConnectionStatus}");

        _stats.Connected(Stopwatch.GetElapsedTime(connectStarted));
        _isCounted = true;
        _coordinator.StateChanged += state =>
        {
            // Counted once per drop and once per return, whatever states lie between
            if (state == CoordinatorState.Connected && !_isCounted)
            {
                _isCounted = true;
                _stats.Connected(TimeSpan.Zero);
            }
            else if (state != CoordinatorState.Connected && _isCounted)
            {
                _isCounted = false;
                _stats.Disconnected();
            }
        };

        SaveIdentity(_coordinator.GetCurrentUserCredentials().UserIdentity);
        Log($"connected as {_coordinator.GetCurrentUser().Username}");

        if (_options.ChatInterval is { } chatInterval)
        {
            await PrepareChatAsync(_coordinator);
            _ = ChatForeverAsync(chatInterval, cancellationToken);
        }

        if (!_options.Voice)
        {
            await WaitForeverAsync(cancellationToken);
            return;
        }

        _sessionModel = await FindSessionAsync(_coordinator);
        _isScreenWanted = _options.Screen;
        _isWatchWanted = _options.Watch;

        await JoinSessionAsync(cancellationToken);

        if (!_options.Quiet)
            _ = ReportVideoForeverAsync(cancellationToken);

        if (_options.ScreenInterval is { } screenInterval)
            _ = ToggleScreenForeverAsync(screenInterval, cancellationToken);

        if (_options.WatchInterval is { } watchInterval)
            _ = ToggleWatchForeverAsync(watchInterval, cancellationToken);

        if (_options.RejoinInterval is { } rejoinInterval)
            _ = RejoinForeverAsync(rejoinInterval, cancellationToken);

        try
        {
            if (_options.MuteInterval is { } interval && !IsListener)
                await ToggleMuteForeverAsync(interval, cancellationToken);
            else
                await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WaitForeverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Joins the session with a transport of its own and starts the voice, and the screen if it is wanted.
    /// </summary>
    private async Task JoinSessionAsync(CancellationToken cancellationToken)
    {
        var coordinator = _coordinator!;
        var sessionModel = _sessionModel!;
        var session = new HybridSession(sessionModel, coordinator, _transports,
            _services.GetRequiredService<ILogger<HybridSession>>(), _options.Listen, voiceActivity: _voiceActivity);

        _session = session;

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += state =>
        {
            if (state == HybridSessionState.Connected)
                connected.TrySetResult(true);
            else if (state is HybridSessionState.Failed or HybridSessionState.Disconnected)
                connected.TrySetResult(false);
        };
        session.ErrorOccurred += message => Log($"session error: {message}");
        session.ScreenShareChanged += (userId, isSharing) =>
        {
            Log($"user {userId} {(isSharing ? "started" : "stopped")} sharing a screen");
            OnScreenShareChanged(session, userId, isSharing);
        };
        session.VideoFrameReady += OnVideoFrame;

        var joinStarted = Stopwatch.GetTimestamp();
        var (joinResult, transport) = await coordinator.JoinHybridSession(sessionModel.Id, session.ClientEpoch);

        if (joinResult != HybridSessionJoinResult.NoError || transport == null)
            throw new InvalidOperationException($"{Name}: failed to join \"{sessionModel.Name}\": {joinResult}");

        session.Connect(transport);

        if (await Task.WhenAny(connected.Task, Task.Delay(ConnectTimeout, cancellationToken)) != connected.Task ||
            !connected.Task.Result)
            throw new InvalidOperationException($"{Name}: the WebRTC transport did not connect");

        _stats.Joined(Stopwatch.GetElapsedTime(joinStarted));
        _isJoinCounted = true;
        Log($"joined \"{sessionModel.Name}\"");

        var voiceResult = await session.StartVoiceShareAsync(_input, _options.Volume);

        if (voiceResult != StartVoiceShareResult.NoError)
            throw new InvalidOperationException($"{Name}: failed to start the voice: {voiceResult}");

        CountVoice(true);

        Log($"playing {Describe(_input)}");
        await MuteIfListenerAsync();

        if (_isScreenWanted)
            await StartScreenAsync();

        await WatchSharedScreensAsync(session, await FindSharedScreensAsync(coordinator, sessionModel.Id));

        // A rejoin after a transport failure releases the producers, so both are started again
        session.StateChanged += state =>
        {
            if (state != HybridSessionState.Connected || session != _session)
                return;

            if (!session.IsVoiceStarted)
                _ = RestartVoiceAsync();

            if (_isScreenWanted && !session.IsScreenShareStarted)
                _ = StartScreenAsync();

            // The consumers of the screens went with the old transport as well
            _ = WatchSharedScreensAsync(session, GetSharingUsers());
        };
    }

    /// <summary>
    /// The users who showed a screen before we joined: those are known from the peers only.
    /// </summary>
    private async Task<int[]> FindSharedScreensAsync(ICoordinatorSession coordinator, int sessionId)
    {
        var (result, peers) = await coordinator.GetAllHybridSessionPeers(sessionId);

        if (result != PeerListRequestResult.NoError || peers == null)
            return [];

        var self = coordinator.GetCurrentUser().Id;
        var sharing = peers.Values
            .Where(peer => peer.IsScreenShareActive && peer.UserId != self)
            .Select(peer => peer.UserId)
            .ToArray();

        lock (_sharingUsersLock)
            _sharingUsers.UnionWith(sharing);

        return sharing;
    }

    private int[] GetSharingUsers()
    {
        lock (_sharingUsersLock)
            return _sharingUsers.ToArray();
    }

    // Raised on any thread, for the others and for ourselves
    private void OnScreenShareChanged(HybridSession session, int userId, bool isSharing)
    {
        if (session != _session || userId == _coordinator!.GetCurrentUser().Id)
            return;

        lock (_sharingUsersLock)
        {
            if (isSharing)
                _sharingUsers.Add(userId);
            else
                _sharingUsers.Remove(userId);
        }

        if (isSharing)
            _ = WatchSharedScreensAsync(session, [userId]);
    }

    /// <summary>
    /// Asks for the screens of the given users, unless watching is off.
    /// </summary>
    private async Task WatchSharedScreensAsync(HybridSession session, IEnumerable<int> userIds)
    {
        if (!_isWatchWanted)
            return;

        foreach (var userId in userIds)
        {
            if (session.IsWatchingScreen(userId))
                continue;

            var result = await session.SetScreenWatchAsync(userId, true);

            Log(result == SetScreenShareWatchResult.NoError
                ? $"watching the screen of user {userId}"
                : $"failed to watch the screen of user {userId}: {result}");
        }
    }

    private async Task ToggleWatchForeverAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);

                _isWatchWanted = !_isWatchWanted;

                var session = _session!;

                if (_isWatchWanted)
                {
                    await WatchSharedScreensAsync(session, GetSharingUsers());
                    continue;
                }

                foreach (var userId in GetSharingUsers())
                {
                    var result = await session.SetScreenWatchAsync(userId, false);

                    Log(result == SetScreenShareWatchResult.NoError
                        ? $"stopped watching the screen of user {userId}"
                        : $"failed to stop watching the screen of user {userId}: {result}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Leaves the session and joins it again every <paramref name="interval"/>, each time with a new transport.
    /// </summary>
    private async Task RejoinForeverAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            for (var count = 1; ; count++)
            {
                await Task.Delay(interval, cancellationToken);

                // The transport goes first, see StopAsync
                _session?.Dispose();
                _session = null;
                CountLeft();
                await _coordinator!.LeaveSession(_sessionModel!.Id);

                await JoinSessionAsync(cancellationToken);
                Log($"rejoined {count} time(s)");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log($"failed to rejoin: {exception.Message}");
        }
    }

    private async Task RestartVoiceAsync()
    {
        var result = await _session!.StartVoiceShareAsync(_input, _options.Volume);

        if (result == StartVoiceShareResult.NoError)
            CountVoice(true);

        Log(result == StartVoiceShareResult.NoError
            ? $"reconnected, playing {Describe(_input)}"
            : $"reconnected, but failed to start the voice: {result}");
        await MuteIfListenerAsync();
    }

    private bool IsListener => _index >= (_options.Speakers ?? int.MaxValue);

    private async Task MuteIfListenerAsync()
    {
        if (!IsListener || _session!.IsMicrophoneMuted)
            return;

        var result = await _session.SetMicrophoneMutedAsync(true);
        Log(result == SetMicrophoneMuteResult.NoError ? "muted: listener" : $"failed to mute: {result}");
    }

    private async Task StartScreenAsync()
    {
        // Another pitch than the voice tone, so the two can be told apart by ear
        var result = await _session!.StartScreenShareAsync(new TestPatternVideoInput(),
            new ScreenShareSettings { Framerate = _options.ScreenFramerate },
            _options.ScreenAudio ? new ToneAudioInput(660) : null);

        if (result == StartScreenShareResult.NoError)
            CountScreen(true);

        Log(result == StartScreenShareResult.NoError
            ? _options.ScreenAudio ? "sharing a test picture with a tone" : "sharing a test picture"
            : $"failed to share the screen: {result}");
    }

    private async Task ToggleScreenForeverAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);

                _isScreenWanted = !_isScreenWanted;

                if (_isScreenWanted)
                {
                    await StartScreenAsync();
                    continue;
                }

                var result = await _session!.StopScreenShareAsync();

                if (result == StopScreenShareResult.NoError)
                    CountScreen(false);

                Log(result == StopScreenShareResult.NoError
                    ? "stopped sharing the screen"
                    : $"failed to stop sharing the screen: {result}");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnVideoFrame(int userId, MediaSourceType source, RawVideoFrame frame)
    {
        _stats.VideoFrame();

        lock (_receivedVideoLock)
        {
            _receivedVideo.TryGetValue(userId, out var received);
            _receivedVideo[userId] = (received.Count + 1, frame.Width, frame.Height);
        }
    }

    /// <summary>
    /// Tells every few seconds whose video arrives and how fast, and whose audio is heard.
    /// </summary>
    private async Task ReportVideoForeverAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(5);

        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);

                KeyValuePair<int, (int Count, int Width, int Height)>[] received;

                lock (_receivedVideoLock)
                {
                    received = _receivedVideo.ToArray();
                    _receivedVideo.Clear();
                }

                foreach (var (userId, video) in received)
                    Log($"video of user {userId}: {video.Width}x{video.Height}, " +
                        $"{video.Count / interval.TotalSeconds:0.#} fps");

                int[] heard;

                lock (_heardUsersLock)
                    heard = _heardUsers.Order().ToArray();

                Log($"audio of users: {(heard.Length == 0 ? "none" : string.Join(", ", heard))}");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ToggleMuteForeverAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(interval, cancellationToken);

            var mute = !_session!.IsMicrophoneMuted;
            var result = await _session.SetMicrophoneMutedAsync(mute);

            Log(result == SetMicrophoneMuteResult.NoError
                ? (mute ? "muted" : "unmuted")
                : $"failed to {(mute ? "mute" : "unmute")}: {result}");
        }
    }

    /// <summary>
    /// Leaves the session and disconnects. Safe to call after a failed <see cref="RunAsync"/>.
    /// </summary>
    public async Task StopAsync()
    {
        // The transport goes first: once the server handles the leave it closes its side, and a
        // transport still alive by then reports ICE failed and starts a reconnect
        _session?.Dispose();
        CountLeft();

        if (_isCounted)
        {
            _isCounted = false;
            _stats.Disconnected();
        }

        try
        {
            if (_session != null && _coordinator != null && _sessionModel != null)
                await _coordinator.LeaveSession(_sessionModel.Id);
        }
        catch (Exception exception)
        {
            Log($"failed to leave the session: {exception.Message}");
        }

        try
        {
            if (_coordinator is { ConnectionStatus: CoordinatorState.Connected })
                await _coordinator.Disconnect();
        }
        catch (Exception exception)
        {
            Log($"failed to disconnect: {exception.Message}");
        }

        Log("stopped");
    }

    private async Task<HybridSessionModel> FindSessionAsync(ICoordinatorSession coordinator)
    {
        var (result, sessions, _) = await coordinator.GetAllSessions();

        if (result != SessionRequestResult.NoError || sessions == null)
            throw new InvalidOperationException($"{Name}: failed to get the session list: {result}");

        var voiceSessions = sessions.Values.OfType<HybridSessionModel>().ToList();

        if (voiceSessions.Count == 0)
            throw new InvalidOperationException($"{Name}: the server has no voice sessions");

        if (_options.Session == null)
            return voiceSessions[0];

        return voiceSessions.FirstOrDefault(session => session.Id.ToString() == _options.Session)
               ?? voiceSessions.FirstOrDefault(session =>
                   string.Equals(session.Name, _options.Session, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"{Name}: no voice session \"{_options.Session}\". Available: " +
                   string.Join(", ", voiceSessions.Select(session => $"{session.Id} \"{session.Name}\"")));
    }

    // Each bot keeps its own user between runs instead of registering a new one every time
    private string IdentityPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowRate", "TestBot", $"identity-{_options.Host}-{_options.Port}-{_index + 1}.txt");

    private string? LoadIdentity()
    {
        try
        {
            return File.Exists(IdentityPath) ? File.ReadAllText(IdentityPath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void SaveIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IdentityPath)!);
            File.WriteAllText(IdentityPath, identity);
        }
        catch (IOException exception)
        {
            Log($"failed to save the identity: {exception.Message}");
        }
    }

    private static string Describe(AudioInput input) => input switch
    {
        FileAudioInput file => $"file {file.PreparedPcmPath}",
        ToneAudioInput tone => $"a {tone.Frequency} Hz tone",
        _ => input.ToString()
    };

    // Whether this bot is counted in the stats as connected, joined, speaking and sharing
    private bool _isCounted;
    private bool _isJoinCounted;
    private bool _isVoiceCounted;
    private bool _isScreenCounted;

    private void CountVoice(bool isStarted)
    {
        if (_isVoiceCounted == isStarted)
            return;

        _isVoiceCounted = isStarted;

        if (isStarted)
            _stats.VoiceStarted();
        else
            _stats.VoiceStopped();
    }

    private void CountScreen(bool isShared)
    {
        if (_isScreenCounted == isShared)
            return;

        _isScreenCounted = isShared;
        _stats.ScreenShared(isShared);
    }

    // The producers go with the transport, so leaving stops the voice and the screen as well
    private void CountLeft()
    {
        CountVoice(false);
        CountScreen(false);

        if (!_isJoinCounted)
            return;

        _isJoinCounted = false;
        _stats.Left();
    }

    private void Log(string message)
    {
        // Quiet bots still report what went wrong, only their progress is dropped
        if (!_options.Quiet || message.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("error", StringComparison.OrdinalIgnoreCase))
            LogBotMessage(Name, message);
    }

    // The bot reports progress as free text, it is a console tool
    [LoggerMessage(LogLevel.Information, "[{Bot}] {Message}")]
    private partial void LogBotMessage(string bot, string message);
}
