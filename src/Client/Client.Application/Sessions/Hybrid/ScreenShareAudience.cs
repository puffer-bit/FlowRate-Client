using Client.Core.Coordinator;
using Client.Core.Sessions.Hybrid;
using Shared.Models.Media;

namespace Client.Application.Sessions.Hybrid;

/// <summary>Screen viewers for one session. Accessed on the UI thread.</summary>
public sealed class ScreenShareAudience(int sessionId)
{
    private readonly Dictionary<int, HashSet<int>> _viewers = [];
    private readonly List<HybridSessionPeerEvent> _pending = [];
    private bool _hasSnapshot;

    public IEnumerable<int> GetViewers(int sharerId) =>
        _viewers.TryGetValue(sharerId, out var viewers) ? viewers : [];

    public void LoadSnapshot(IEnumerable<PeerModel> peers)
    {
        _viewers.Clear();
        foreach (var peer in peers)
            foreach (var sharerId in peer.WatchedScreenShares)
                SetWatching(peer.UserId, sharerId, true);

        _hasSnapshot = true;
        foreach (var change in _pending)
            Apply(change);
        _pending.Clear();
    }

    public void Apply(HybridSessionPeerEvent change)
    {
        if (change.SessionId != sessionId)
            return;

        if (!_hasSnapshot)
        {
            _pending.Add(change);
            return;
        }

        switch (change)
        {
            case PeerScreenShareWatchChangedEvent watching:
                SetWatching(watching.UserId, watching.SharerUserId, watching.IsWatching);
                break;
            case PeerLeftEvent left:
                _viewers.Remove(left.UserId);
                foreach (var viewers in _viewers.Values)
                    viewers.Remove(left.UserId);
                break;
            case PeerTrackStoppedEvent { SourceType: MediaSourceType.Screen } stopped:
                _viewers.Remove(stopped.UserId);
                break;
            case PeerJoinedEvent joined:
                foreach (var viewers in _viewers.Values)
                    viewers.Remove(joined.UserId);
                foreach (var sharerId in joined.Peer.WatchedScreenShares)
                    SetWatching(joined.UserId, sharerId, true);
                break;
        }
    }

    private void SetWatching(int viewerId, int sharerId, bool watching)
    {
        // Local previews are never remote viewers.
        if (viewerId == sharerId)
            return;

        if (watching)
        {
            if (!_viewers.TryGetValue(sharerId, out var viewers))
                _viewers[sharerId] = viewers = [];
            viewers.Add(viewerId);
        }
        else if (_viewers.TryGetValue(sharerId, out var viewers))
            viewers.Remove(viewerId);
    }
}
