using Client.Core.Events;
using Shared.Models.Media;
using Client.Core.Users;
using Shared.Enums;


namespace Client.Application.Sessions.Hybrid;

public partial class HybridSession
{
    private readonly Lock _peerEventsLock = new();
    private Task _peerEvents = Task.CompletedTask;

    /// <summary>
    /// Keeps the transport in step with the tracks the other peers send. Events of other sessions
    /// come through the same coordinator and are skipped.
    /// </summary>
    /// <remarks>
    /// The events are handled one after another in the order they arrive: a track waits for the
    /// volume of its user, and a stop that overtook its start would leave the track playing.
    /// </remarks>
    private void HandleHybridSessionEvent(HybridSessionPeerEvent peerEvent)
    {
        if (peerEvent.SessionId != Model.Id)
            return;

        if (peerEvent is not (PeerTrackStartedEvent or PeerTrackStoppedEvent or PeerLeftEvent
            or PeerScreenShareStartedEvent))
            return;

        _ = EnqueuePeerWork(async () =>
        {
            await ProcessPeerEventAsync(peerEvent);
            return true;
        });
    }

    /// <summary>
    /// Follows the SFU node of the session. The server keeps us in the session while it has no node,
    /// so we wait for one and rejoin by ourselves once it is there.
    /// </summary>
    private void HandleNodeEvent(HybridSessionNodeEvent nodeEvent)
    {
        if (nodeEvent.SessionId != Model.Id || IsDisposed)
            return;

        switch (nodeEvent)
        {
            case SessionNodeStateChangedEvent { State: HybridSessionNodeState.WaitingForSfuNode }:
                _isWaitingForNode = true;
                _isRejoinRequired = true;
                ScheduleReconnect("the session waits for an SFU node");
                break;

            case SessionNodeStateChangedEvent:
                _isWaitingForNode = false;
                break;

            // What the old node held for us is gone, even if our transport still looks connected
            case SessionNodeReplacedEvent:
                _isRejoinRequired = true;
                ScheduleReconnect("the SFU node of the session was replaced");
                break;
        }

        // A loop that already runs takes the change on its next round, without finishing its pause
        WakeReconnect();
    }

    /// <summary>
    /// Runs <paramref name="work"/> after everything queued before it, whether that failed or not.
    /// </summary>
    private Task<T> EnqueuePeerWork<T>(Func<Task<T>> work)
    {
        lock (_peerEventsLock)
        {
            var task = _peerEvents
                .ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default)
                .Unwrap();

            _peerEvents = task;

            return task;
        }
    }

    private async Task ProcessPeerEventAsync(HybridSessionPeerEvent peerEvent)
    {
        if (IsDisposed)
            return;

        try
        {
            switch (peerEvent)
            {
                case PeerTrackStartedEvent started:
                    // Given with the track, so it never plays at the default volume
                    var channel = ChannelOf(started.Track.SourceType);
                    _webRTCService.RegisterInbound(started.Track, await GetPeerVolumeAsync(started.UserId, channel));

                    // A slider moved while the track was being registered was skipped by the transport
                    if (_peerPlayback != null)
                        _webRTCService.SetInboundVolume(started.UserId, started.Track.SourceType,
                            _peerPlayback.Get(started.UserId, channel).EffectiveVolume);

                    break;

                // Only announced: the track of a screen comes when we ask to watch it. Our own
                // start is reported by the call that made it.
                case PeerScreenShareStartedEvent started:
                    if (started.UserId != _coordinatorSession.GetCurrentUser().Id)
                        ScreenShareChanged?.Invoke(started.UserId, true);

                    break;

                // A stopped screen is only paused on the SFU and comes back in the same track. Its
                // section stays: every track that is released and registered again costs webrtcbin a
                // new pad and jitter buffer, which it never gives back. The track goes with its peer.
                // Watching ends with the show, the next one has to be asked for again.
                case PeerTrackStoppedEvent { SourceType: MediaSourceType.Screen } stopped:
                    if (stopped.UserId != _coordinatorSession.GetCurrentUser().Id)
                    {
                        SetScreenWatched(stopped.UserId, false);
                        ScreenShareChanged?.Invoke(stopped.UserId, false);
                    }

                    break;

                case PeerLeftEvent left:
                    _webRTCService.UnregisterAllUserInbounds(left.UserId);
                    SetScreenWatched(left.UserId, false);

                    break;

                case PeerTrackStoppedEvent stopped:
                    _webRTCService.UnregisterInbound(stopped.UserId, stopped.MediaType, stopped.SourceType);

                    break;
            }
        }
        catch (Exception exception)
        {
            // Nobody awaits the chain, and a fault would not stop the next events anyway
            LogPeerEventFailed(exception, Model.Id, peerEvent.GetType().Name);
        }
    }

    private async Task<double> GetPeerVolumeAsync(int userId, PeerAudioChannel channel)
    {
        if (_peerPlayback == null)
            return PeerPlayback.Default.EffectiveVolume;

        await _peerPlayback.LoadAsync(_coordinatorSession.GetCurrentUser().CoordinatorInstanceId, [userId]);

        return _peerPlayback.Get(userId, channel).EffectiveVolume;
    }

    // The sound of a shown screen plays on its own settings, everything else a user sends is the voice
    private static PeerAudioChannel ChannelOf(MediaSourceType sourceType) =>
        sourceType == MediaSourceType.Screen ? PeerAudioChannel.Screen : PeerAudioChannel.Voice;

    // Raised on any thread, for the peers of every session; the transport skips users without tracks here
    private void OnPeerPlaybackChanged(int userId, PeerAudioChannel channel, PeerPlayback playback)
    {
        _webRTCService.SetInboundVolume(userId,
            channel == PeerAudioChannel.Screen ? MediaSourceType.Screen : MediaSourceType.Microphone,
            playback.EffectiveVolume);

        if (channel != PeerAudioChannel.Voice)
            return;

        // Muting hides the indicator at once, unmuting shows it again if the user is still speaking
        lock (_speakingLock)
        {
            if (_speakingUsers.Contains(userId))
                PublishSpeaking(userId);
        }
    }
}
