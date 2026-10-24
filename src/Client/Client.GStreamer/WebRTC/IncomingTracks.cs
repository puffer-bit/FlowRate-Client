using System.Collections.Concurrent;
using Client.Core.Sessions.Hybrid;
using Microsoft.Extensions.Logging;
using Shared.Models.Media;
using Client.GStreamer.WebRTC.Sdp;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// The tracks the SFU sends: which section carries which, and the playback volume of their users.
/// </summary>
internal sealed partial class IncomingTracks
{
    private readonly TransportCore _core;
    private readonly MediaSections _sections;
    private readonly Func<bool> _negotiate;
    private readonly Action<IReadOnlyList<string>> _inboundsReady;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, InboundTrack> _inbounds = new();

    // Playback volume per user and source with inbound tracks: the voice and the sound of a screen
    // are set apart. Dropped together with the last track of the user from that source.
    private readonly ConcurrentDictionary<(int UserId, MediaSourceType SourceType), double> _inboundVolumes = new();

    // Set while the user hears no voices. The sound of a shown screen is not a voice and plays on
    private volatile bool _isVoiceMuted;

    // The tracks whose sections webrtcbin knows, which the SFU was told about
    private readonly ConcurrentDictionary<string, bool> _readyInbounds = new();

    /// <param name="negotiate">Runs a negotiation round after the set of sections changed.</param>
    /// <param name="inboundsReady">Receives the ids of the tracks that can be received from now on.</param>
    public IncomingTracks(TransportCore core, MediaSections sections, Func<bool> negotiate,
        Action<IReadOnlyList<string>> inboundsReady, ILogger logger)
    {
        _core = core;
        _sections = sections;
        _negotiate = negotiate;
        _inboundsReady = inboundsReady;
        _logger = logger;
    }

    /// <summary>
    /// Reports the tracks of a finished negotiation round that were not ready before it. A track
    /// that was released while the round ran is left out.
    /// </summary>
    public void OnRoundCompleted(IReadOnlyList<SdpMediaSection> sections)
    {
        var ready = sections
            .Select(section => section.InboundId)
            .OfType<string>()
            .Where(inboundId => _inbounds.ContainsKey(inboundId) && _readyInbounds.TryAdd(inboundId, true))
            .ToList();

        if (ready.Count == 0)
            return;

        LogInboundsReady(_core.SessionId, ready.Count);
        _inboundsReady(ready);
    }

    public InboundTrack? Find(string inboundId) => _inbounds.GetValueOrDefault(inboundId);

    public InboundTrack? FindBySsrc(uint ssrc) => _inbounds.Values.FirstOrDefault(inbound => inbound.SSRC == ssrc);

    /// <summary>
    /// The track the section carries now. A section changes hands when it is reused, so it is
    /// matched to its track at the moment of asking. Only inside <see cref="MediaSections.Enter"/>.
    /// </summary>
    public InboundTrack? FindBySection(uint mline) =>
        _sections.GetInboundId(mline) is { } inboundId ? _inbounds.GetValueOrDefault(inboundId) : null;

    /// <summary>
    /// Registers an inbound track and renegotiates, because every consumer of the SFU needs a
    /// section of its own.
    /// </summary>
    /// <param name="volume">
    /// Playback volume of the user's tracks of the same source, set before the track can play. It
    /// applies to the earlier tracks of that source as well.
    /// </param>
    public void Register(InboundTrack inbound, double volume)
    {
        // Stored first: the pad of a new section asks for it as soon as it appears. A picture has
        // no volume, and must not reset the one of the sound of its screen.
        if (inbound.MediaType == MediaType.Audio)
            _inboundVolumes[(inbound.UserId, inbound.SourceType)] = volume;

        if (!_inbounds.TryAdd(inbound.Id, inbound))
            return;

        // A rejoin makes the SFU create a new consumer for a track we may still hold, and the old
        // section would otherwise stay occupied by a stream that no longer arrives.
        var stale = _inbounds
            .Where(item => item.Key != inbound.Id &&
                           item.Value.UserId == inbound.UserId &&
                           item.Value.MediaType == inbound.MediaType &&
                           item.Value.SourceType == inbound.SourceType)
            .Select(item => item.Key)
            .ToArray();

        int index;

        using (_sections.Enter())
        {
            foreach (var staleId in stale)
            {
                _inbounds.TryRemove(staleId, out _);
                _readyInbounds.TryRemove(staleId, out _);

                var staleSlot = _sections.Slots.FirstOrDefault(item => item.InboundId == staleId);

                if (staleSlot == null)
                    continue;

                staleSlot.InboundId = null;

                _core.Player?.Retire((uint)_sections.Slots.IndexOf(staleSlot));
            }

            var slot = _sections.ClaimReceiving(inbound.MediaType, inbound.MimeType);
            slot.InboundId = inbound.Id;
            index = _sections.Slots.IndexOf(slot);
        }

        LogInboundRegistered(_core.SessionId, inbound.MediaType, inbound.UserId, inbound.SSRC);

        // A reused section still plays at the volume of the user it carried before
        if (inbound.MediaType == MediaType.Audio)
            _core.Player?.SetVolume((uint)index, GetVolume(inbound));

        _negotiate();
    }

    /// <summary>
    /// Releases the section of a track. The section itself is kept as inactive so that the mid of
    /// every other section stays the same, and is reused by the next track of the same kind.
    /// </summary>
    public void Unregister(int userId, MediaType mediaType, MediaSourceType mediaSourceType)
    {
        var target = _inbounds.FirstOrDefault(item =>
            item.Value.UserId == userId &&
            item.Value.MediaType == mediaType &&
            item.Value.SourceType == mediaSourceType);

        if (target.Key == null)
            return;

        ReleaseInbounds([target.Key]);
    }

    public void UnregisterAllOfUser(int userId)
    {
        var targets = _inbounds
            .Where(item => item.Value.UserId == userId)
            .Select(item => item.Key)
            .ToArray();

        ReleaseInbounds(targets);
    }

    /// <summary>
    /// Changes the playback volume of the audio tracks of a user from one source. A user without
    /// inbound tracks of it is skipped, the volume of the next track comes with <see cref="Register"/>.
    /// </summary>
    public void SetVolume(int userId, MediaSourceType sourceType, double volume)
    {
        using (_sections.Enter())
        {
            if (!_inbounds.Values.Any(inbound => inbound.UserId == userId && inbound.SourceType == sourceType))
                return;

            _inboundVolumes[(userId, sourceType)] = volume;
        }

        ApplyVolumes(inbound => inbound.UserId == userId && inbound.SourceType == sourceType);
    }

    /// <summary>
    /// Silences the voices of everybody, or brings them back at the volume of each user. The tracks
    /// that come later follow it as well.
    /// </summary>
    public void SetVoiceMuted(bool isMuted)
    {
        _isVoiceMuted = isMuted;

        ApplyVolumes(inbound => inbound.SourceType == MediaSourceType.Microphone);
    }

    // Gives the player the volume of every audio track that is asked for
    private void ApplyVolumes(Func<InboundTrack, bool> isWanted)
    {
        List<(int Section, double Volume)> sections = [];

        using (_sections.Enter())
        {
            for (var index = 0; index < _sections.Slots.Count; index++)
            {
                var slot = _sections.Slots[index];

                if (!slot.IsSend && slot.InboundId != null &&
                    _inbounds.TryGetValue(slot.InboundId, out var inbound) &&
                    inbound.MediaType == MediaType.Audio && isWanted(inbound))
                    sections.Add((index, GetVolume(inbound)));
            }
        }

        foreach (var (section, volume) in sections)
            _core.Player?.SetVolume((uint)section, volume);
    }

    private double GetVolume(InboundTrack inbound) =>
        _isVoiceMuted && inbound.SourceType == MediaSourceType.Microphone
            ? 0
            : _inboundVolumes.GetValueOrDefault((inbound.UserId, inbound.SourceType), 1.0);

    /// <summary>
    /// Volume of the user whose track the section carries. Called from a streaming thread when the
    /// pad of the section appears.
    /// </summary>
    public double ResolveSectionVolume(uint mline)
    {
        using (_sections.Enter())
        {
            return FindBySection(mline) is { } inbound ? GetVolume(inbound) : 1.0;
        }
    }

    private void ReleaseInbounds(IReadOnlyList<string> inboundIds)
    {
        var released = false;

        foreach (var inboundId in inboundIds)
        {
            if (!_inbounds.TryRemove(inboundId, out var removed))
                continue;

            _readyInbounds.TryRemove(inboundId, out _);

            using (_sections.Enter())
            {
                var slot = _sections.Slots.FirstOrDefault(item => item.InboundId == inboundId);

                if (slot != null)
                {
                    slot.InboundId = null;
                    _core.Player?.Retire((uint)_sections.Slots.IndexOf(slot));
                }

                if (!_inbounds.Values.Any(inbound => inbound.UserId == removed.UserId &&
                                                     inbound.SourceType == removed.SourceType))
                    _inboundVolumes.TryRemove((removed.UserId, removed.SourceType), out _);
            }

            released = true;
        }

        if (released)
            _negotiate();
    }
}
