using System.Globalization;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.WebRTC.Sdp;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Client.LibWebRTC.Transport;

public sealed partial class LibWebRTCTransport
{
    /// <summary>
    /// Runs an offer/answer round over the current sections. A round asked for while another one runs
    /// is coalesced into a single follow-up.
    /// </summary>
    private bool Negotiate()
    {
        IMediaPeer peer;
        string offer;
        List<SendSection> sends;

        lock (_lock)
        {
            if (_isDisposed || _peer == null || _transportParameters == null)
                return false;

            if (_isNegotiating)
            {
                _isNegotiationPending = true;
                return true;
            }

            _isNegotiating = true;
            peer = _peer;

            var sections = BuildSections();

            sends = sections
                .Select((section, index) => (section.Mid, Slot: _slots[index]))
                .Select(item => item.Slot.OutboundId != null && _outbounds.TryGetValue(item.Slot.OutboundId, out var outbound)
                    ? _outgoingVideos.TryGetValue(outbound.Id, out var video)
                        ? new SendSection(item.Mid, outbound.SSRC, video.Source, SendSections.RtxSsrcOf(outbound))
                        : new SendSection(item.Mid, outbound.SSRC, Audio: _outgoingSounds.GetValueOrDefault(outbound.Id)?.Source)
                    : item.Slot.PausedSsrc is { } paused ? new SendSection(item.Mid, paused, RtxSsrc: item.Slot.PausedRtxSsrc) : null)
                .OfType<SendSection>()
                .ToList();

            _roundSections = sections;
            offer = SdpBuilder.Build(_transportParameters, sections);
        }

        LogRemoteDescription(_sessionId, offer);

        try
        {
            peer.Negotiate(offer, sends);
            return true;
        }
        catch (Exception exception)
        {
            LogNegotiationFailed(_sessionId, exception.Message);
            CompleteNegotiation();
            return false;
        }
    }

    /// <summary>
    /// Turns the slots into sections. An empty set still produces one, because a description without
    /// media carries no transport to bring up. Only inside the lock.
    /// </summary>
    private List<SdpMediaSection> BuildSections()
    {
        if (_slots.Count == 0)
            _slots.Add(new Slot { IsPlaceholder = true });

        return _slots.Select((slot, index) =>
        {
            var mid = index.ToString(CultureInfo.InvariantCulture);

            if (slot.IsPlaceholder)
                return new SdpMediaSection { Mid = mid, Kind = AudioKind, Direction = SdpDirection.SendOnly, Codec = RtpCodec.Opus() };

            if (slot.IsSend)
            {
                if (slot.OutboundId != null && _outbounds.TryGetValue(slot.OutboundId, out var outbound))
                {
                    slot.LastCodec = _outgoingVideos.TryGetValue(outbound.Id, out var video)
                        ? SendSections.VideoCodecOf(outbound, video)
                        : SendSections.SoundCodecOf(outbound);
                    return SendSections.Section(mid, slot.Kind, slot.LastCodec, _transportCcExtensionId);
                }

                if (slot.PausedSsrc != null && slot.LastCodec != null)
                    return SendSections.Section(mid, slot.Kind, slot.LastCodec, _transportCcExtensionId);
            }
            else if (slot.InboundId != null && _inbounds.TryGetValue(slot.InboundId, out var inbound))
            {
                slot.LastCodec = RtpCodec.FromInbound(inbound);
                return new SdpMediaSection
                {
                    Mid = mid,
                    Kind = slot.Kind,
                    Direction = SdpDirection.SendOnly,
                    Codec = slot.LastCodec,
                    SSRC = inbound.SSRC,
                    InboundId = inbound.Id
                };
            }

            // An m-line keeps its kind for good, so an emptied section of a picture stays a video one
            return new SdpMediaSection
            {
                Mid = mid,
                Kind = slot.Kind,
                Direction = SdpDirection.Inactive,
                Codec = slot.LastCodec ?? (slot.Kind == VideoKind ? RtpCodec.H264() : RtpCodec.Opus())
            };
        }).ToList();
    }

    private void OnNegotiated(string localSdp)
    {
        LogLocalDescription(_sessionId, localSdp);

        bool isFirst;
        List<string> ready;

        lock (_lock)
        {
            isFirst = !_isDtlsPublished;
            _isDtlsPublished = true;

            ready = _roundSections
                .Select(section => section.InboundId)
                .OfType<string>()
                .Where(inboundId => _inbounds.ContainsKey(inboundId) && _readyInbounds.Add(inboundId))
                .ToList();
        }

        // The certificate does not change between rounds, so the SFU is told about it once
        if (isFirst)
        {
            if (LocalDtlsParameters.Parse(localSdp) is { } dtls)
                LocalDtlsParametersReady?.Invoke(dtls);
            else
                LogLocalFingerprintMissing(_sessionId);
        }

        // The SFU starts the tracks only once it hears of them, so nothing may come in the way of this
        if (ready.Count > 0)
            InboundsReady?.Invoke(ready);

        try
        {
            // The receivers of the new sections exist only now
            ApplyVolumes();
        }
        finally
        {
            CompleteNegotiation();
        }
    }

    private void OnNegotiationFailed(string error)
    {
        LogNegotiationFailed(_sessionId, error);
        CompleteNegotiation();
    }

    private void CompleteNegotiation()
    {
        bool isPending;

        lock (_lock)
        {
            _isNegotiating = false;
            isPending = _isNegotiationPending;
            _isNegotiationPending = false;
        }

        if (isPending)
            Task.Run(Negotiate);
    }
}
