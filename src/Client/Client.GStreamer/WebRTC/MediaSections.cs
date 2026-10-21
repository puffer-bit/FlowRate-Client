using Client.Core.Sessions.Hybrid;
using Client.GStreamer.WebRTC.Sdp;
using Shared.Models.Media;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// The m-sections of the description, in the order of their m-lines. A section is never removed,
/// only emptied and given to the next track of its kind, which keeps every mid stable across
/// renegotiations.
/// </summary>
/// <remarks>
/// A section is tied to what the other parts know about its track, so they read and change the
/// slots inside <see cref="Enter"/>, together with their own state.
/// </remarks>
internal sealed class MediaSections
{
    private readonly Lock _lock = new();

    /// <summary>
    /// The slots by m-line index. Only inside <see cref="Enter"/>.
    /// </summary>
    public List<MediaSlot> Slots { get; } = [];

    public Lock.Scope Enter() => _lock.EnterScope();

    /// <summary>
    /// A free receiving section of the kind, or a new one. Only inside <see cref="Enter"/>.
    /// </summary>
    public MediaSlot ClaimReceiving(MediaType mediaType)
    {
        var slot = Slots.FirstOrDefault(item => !item.IsPlaceholder && !item.IsSend &&
                                                item.InboundId == null && item.Kind == ToKind(mediaType));

        if (slot == null)
        {
            slot = new MediaSlot { Kind = ToKind(mediaType) };
            Slots.Add(slot);
        }

        return slot;
    }

    /// <summary>
    /// A free sending section of the kind, or a new one. Only inside <see cref="Enter"/>.
    /// </summary>
    /// <remarks>
    /// Sending sections are only reused for sending: a receiving section still has the pad and the
    /// player of its previous stream.
    /// </remarks>
    public MediaSlot ClaimSending(MediaType mediaType)
    {
        var slot = Slots.FirstOrDefault(item => item.IsSend && item.OutboundId == null && item.Kind == ToKind(mediaType));

        if (slot == null)
        {
            slot = new MediaSlot { Kind = ToKind(mediaType), IsSend = true };
            Slots.Add(slot);
        }

        return slot;
    }

    /// <summary>
    /// The id of the inbound track the section carries now. Only inside <see cref="Enter"/>.
    /// </summary>
    public string? GetInboundId(uint mline) => mline < Slots.Count ? Slots[(int)mline].InboundId : null;

    /// <summary>
    /// Turns the registered tracks into sections. An empty set still produces one section, because
    /// a description without media carries no transport to bring up.
    /// </summary>
    public List<SdpMediaSection> Build(Func<string, InboundTrack?> findInbound,
        Func<string, OutboundTrack?> findOutbound)
    {
        lock (_lock)
        {
            // webrtcbin locks the media kind of an m-line once it has been negotiated, so the section
            // that brings the transport up keeps m-line 0 for good instead of being replaced by the
            // first real track.
            if (Slots.Count == 0)
                Slots.Add(new MediaSlot { Kind = "video", IsPlaceholder = true });

            return Slots
                .Select((slot, index) =>
                {
                    if (slot.IsPlaceholder)
                    {
                        return new SdpMediaSection
                        {
                            Mid = index.ToString(),
                            Kind = slot.Kind,
                            Direction = SdpDirection.SendOnly,
                            Codec = RtpCodec.H264()
                        };
                    }

                    if (slot.IsSend)
                    {
                        if (slot.OutboundId != null && findOutbound(slot.OutboundId) is { } outbound)
                        {
                            slot.LastCodec = RtpCodec.FromOutbound(outbound);

                            return new SdpMediaSection
                            {
                                Mid = index.ToString(),
                                Kind = slot.Kind,
                                Direction = SdpDirection.RecvOnly,
                                Codec = slot.LastCodec
                            };
                        }

                        return new SdpMediaSection
                        {
                            Mid = index.ToString(),
                            Kind = slot.Kind,
                            Direction = SdpDirection.Inactive,
                            Codec = slot.LastCodec ?? (slot.Kind == "video" ? RtpCodec.H264() : RtpCodec.Opus(100))
                        };
                    }

                    if (slot.InboundId == null || findInbound(slot.InboundId) is not { } inbound)
                    {
                        return new SdpMediaSection
                        {
                            Mid = index.ToString(),
                            Kind = slot.Kind,
                            Direction = SdpDirection.Inactive,
                            Codec = slot.LastCodec ?? (slot.Kind == "video" ? RtpCodec.H264() : RtpCodec.Opus())
                        };
                    }

                    slot.LastCodec = RtpCodec.FromInbound(inbound);

                    return new SdpMediaSection
                    {
                        Mid = index.ToString(),
                        Kind = slot.Kind,
                        Direction = SdpDirection.SendOnly,
                        Codec = slot.LastCodec,
                        SSRC = inbound.SSRC,
                        InboundId = inbound.Id
                    };
                })
                .ToList();
        }
    }

    private static string ToKind(MediaType mediaType) => mediaType == MediaType.Audio ? "audio" : "video";
}

/// <summary>
/// One m-section slot. Slots are never removed, only emptied and reused, which keeps the mids
/// stable across renegotiations.
/// </summary>
internal sealed class MediaSlot
{
    public required string Kind { get; init; }

    /// <summary>
    /// Whether the section carries one of our tracks to the SFU rather than one from it.
    /// </summary>
    public bool IsSend { get; init; }

    /// <summary>
    /// The section of m-line 0 that exists only to bring the transport up. It never carries a track.
    /// </summary>
    public bool IsPlaceholder { get; init; }

    public string? InboundId { get; set; }

    public string? OutboundId { get; set; }

    /// <summary>
    /// The codec of the track the section carried last. An emptied section keeps offering it:
    /// webrtcbin rejects a section whose codec no longer matches its transceiver, and a
    /// rejected section cannot be brought back, the next track in it fails to negotiate.
    /// </summary>
    public RtpCodec? LastCodec { get; set; }
}
