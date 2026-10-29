using System.Collections.Generic;
using System.Linq;
using Client.Core.Sessions.Hybrid;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// The statistics of every user of the hybrid session we are in, for the lists that show its peers.
/// Fed by the session with every reading of its transport, so a user's history is there before their
/// statistics are opened. Used on the UI thread only.
/// </summary>
public sealed class PeerStatsService
{
    private readonly Dictionary<(int SessionId, int UserId), PeerStatsVM> _peers = [];

    /// <summary>
    /// The statistics of a user, empty until the session reads its transport.
    /// </summary>
    public PeerStatsVM Get(int sessionId, int userId)
    {
        if (!_peers.TryGetValue((sessionId, userId), out var peer))
            _peers[(sessionId, userId)] = peer = new PeerStatsVM();

        return peer;
    }

    /// <summary>
    /// Takes a reading of the session for every user it has a stream of, and for the ones already
    /// known, so their graphs move on while they send nothing.
    /// </summary>
    public void Add(int sessionId, TransportStatsSample sample)
    {
        foreach (var stream in sample.Inbound ?? [])
            Get(sessionId, stream.UserId);

        foreach (var ((session, userId), peer) in _peers)
        {
            if (session == sessionId)
                peer.Add(sample, userId);
        }
    }

    /// <summary>
    /// Forgets the users of a session once it is left.
    /// </summary>
    public void Clear(int sessionId)
    {
        foreach (var key in _peers.Keys.Where(key => key.SessionId == sessionId).ToList())
            _peers.Remove(key);
    }
}
