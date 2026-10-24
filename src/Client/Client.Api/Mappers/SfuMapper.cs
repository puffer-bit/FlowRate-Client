using Shared.Models.Media;
using Shared.Models.Media.Transport;
using Client.Core.Sessions.Hybrid;

namespace Client.Api.Mappers;

internal static class SfuMapper
{
    public static InboundTrack ToModel(this Inbound inbound) => new()
    {
        Id = inbound.Id,
        UserId = inbound.UserId,
        SSRC = inbound.SSRC,
        SourceType = inbound.SourceType,
        MediaType = inbound.MediaType,
        MimeType = inbound.MimeType,
        PayloadType = inbound.PayloadType,
        ClockRate = inbound.ClockRate,
        Channels = inbound.Channels
    };

    public static OutboundTrack ToModel(this Outbound outbound) => new()
    {
        Id = outbound.Id,
        UserId = outbound.UserId,
        SSRC = outbound.SSRC,
        SourceType = outbound.SourceType,
        MediaType = outbound.MediaType,
        MimeType = outbound.MimeType,
        PayloadType = outbound.PayloadType,
        ClockRate = outbound.ClockRate,
        Channels = outbound.Channels,
        RtxPayloadType = outbound.RtxPayloadType,
        RtxSSRC = outbound.RtxSSRC
    };

    public static TransportParameters ToModel(this TransportData transportData) => new(
        transportData.Host,
        transportData.Port,
        transportData.DTLSData.ToModel(),
        new IceParameters(
            transportData.ICEData.UsernameFragment,
            transportData.ICEData.Password,
            transportData.ICEData.ICECandidates
                .Select(candidate => new IceCandidate(candidate.Candidate, candidate.SDPMid, candidate.SDPMLineIndex))
                .ToList()));

    public static DtlsParameters ToModel(this DTLSParameters parameters) => new(
        parameters.Role,
        parameters.Fingerprints
            .Select(fingerprint => new DtlsFingerprint(fingerprint.Algorithm, fingerprint.Fingerprint))
            .ToList());

    public static DTLSParameters ToDto(this DtlsParameters parameters) => new()
    {
        Role = parameters.Role,
        Fingerprints = parameters.Fingerprints
            .Select(fingerprint => new DTLSFingerprint { Algorithm = fingerprint.Algorithm, Fingerprint = fingerprint.Value })
            .ToList()
    };
}
