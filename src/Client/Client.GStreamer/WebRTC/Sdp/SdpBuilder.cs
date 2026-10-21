using System.Text;
using Client.Core.Sessions.Hybrid;

namespace Client.GStreamer.WebRTC.Sdp;

/// <summary>
/// Builds the remote session description for webrtcbin out of the ORTC style parameters the SFU
/// reports. mediasoup itself never sees this text: SDP only exists between this code and
/// webrtcbin, so the description describes the SFU as the remote peer.
/// </summary>
public static class SdpBuilder
{
    private const string LineBreak = "\r\n";

    /// <summary>
    /// Builds the description of the SFU side.
    /// </summary>
    /// <param name="transportData">ICE and DTLS parameters of the server transport.</param>
    /// <param name="sections">One section per consumer or producer, in a stable order.</param>
    /// <param name="setup">
    /// Value of the setup attribute. The SFU reports the DTLS role as auto, so it is offered as
    /// actpass and the answer picks active, which makes us the DTLS client.
    /// </param>
    public static string Build(
        TransportParameters transportData,
        IReadOnlyList<SdpMediaSection> sections,
        string setup = "actpass")
    {
        ArgumentOutOfRangeException.ThrowIfZero(sections.Count, nameof(sections));

        var fingerprint = SelectFingerprint(transportData.Dtls);
        var candidates = BuildCandidates(transportData);

        var sdp = new StringBuilder();

        sdp.Append("v=0").Append(LineBreak);
        sdp.Append("o=- 0 0 IN IP4 0.0.0.0").Append(LineBreak);
        sdp.Append("s=-").Append(LineBreak);
        sdp.Append("t=0 0").Append(LineBreak);

        // mediasoup never sends connectivity checks itself, it only answers them.
        sdp.Append("a=ice-lite").Append(LineBreak);

        // Every section shares a single transport, which is the only way the SFU can keep all
        // clients on one port.
        sdp.Append("a=group:BUNDLE ").Append(string.Join(' ', sections.Select(section => section.Mid))).Append(LineBreak);
        sdp.Append("a=msid-semantic: WMS *").Append(LineBreak);

        foreach (var section in sections)
        {
            AppendMediaSection(sdp, section, transportData, fingerprint, candidates, setup);
        }

        return sdp.ToString();
    }

    private static void AppendMediaSection(
        StringBuilder sdp,
        SdpMediaSection section,
        TransportParameters transportData,
        DtlsFingerprint fingerprint,
        IReadOnlyList<string> candidates,
        string setup)
    {
        var codec = section.Codec;

        sdp.Append($"m={section.Kind} {transportData.Port} UDP/TLS/RTP/SAVPF {codec.PayloadType}").Append(LineBreak);
        sdp.Append($"c=IN IP4 {transportData.Host}").Append(LineBreak);

        // The transport is rtcp-mux only, so RTCP shares the RTP port.
        sdp.Append($"a=rtcp:{transportData.Port} IN IP4 {transportData.Host}").Append(LineBreak);
        sdp.Append("a=rtcp-mux").Append(LineBreak);
        sdp.Append("a=rtcp-rsize").Append(LineBreak);

        sdp.Append($"a=ice-ufrag:{transportData.Ice.UsernameFragment}").Append(LineBreak);
        sdp.Append($"a=ice-pwd:{transportData.Ice.Password}").Append(LineBreak);

        foreach (var candidate in candidates)
            sdp.Append($"a={candidate}").Append(LineBreak);

        sdp.Append("a=end-of-candidates").Append(LineBreak);

        sdp.Append($"a=fingerprint:{fingerprint.Algorithm.ToLowerInvariant()} {fingerprint.Value}").Append(LineBreak);
        sdp.Append($"a=setup:{setup}").Append(LineBreak);
        sdp.Append($"a=mid:{section.Mid}").Append(LineBreak);
        sdp.Append($"a={section.ToDirectionAttribute()}").Append(LineBreak);

        sdp.Append($"a=rtpmap:{codec.ToRtpMap()}").Append(LineBreak);

        if (!string.IsNullOrWhiteSpace(codec.FormatParameters))
            sdp.Append($"a=fmtp:{codec.PayloadType} {codec.FormatParameters}").Append(LineBreak);

        foreach (var feedback in codec.RtcpFeedback)
            sdp.Append($"a=rtcp-fb:{codec.PayloadType} {feedback}").Append(LineBreak);

        // The SSRC is only announced for streams the SFU sends.
        if (section.Direction == SdpDirection.SendOnly && section.SSRC.HasValue)
        {
            sdp.Append($"a=ssrc:{section.SSRC.Value} cname:{section.Cname}").Append(LineBreak);
        }
    }

    private static DtlsFingerprint SelectFingerprint(DtlsParameters dtlsParameters)
    {
        if (dtlsParameters.Fingerprints.Count == 0)
            throw new InvalidOperationException("Transport DTLS parameters contain no fingerprints");

        return dtlsParameters.Fingerprints
                   .FirstOrDefault(item => item.Algorithm.Equals("sha-256", StringComparison.OrdinalIgnoreCase))
               ?? dtlsParameters.Fingerprints[0];
    }

    /// <summary>
    /// Normalizes the candidates reported by the SFU and, when it reported none usable, falls back
    /// to a host candidate built from the transport endpoint.
    /// </summary>
    /// <summary>
    /// Checks the shape of a candidate attribute: foundation, component, protocol, priority,
    /// address, port and the typ keyword.
    /// </summary>
    private static bool IsWellFormedCandidate(string candidate)
    {
        var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 8
               && parts[6].Equals("typ", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(parts[1], out _)
               && long.TryParse(parts[3], out _)
               && int.TryParse(parts[5], out _);
    }

    private static IReadOnlyList<string> BuildCandidates(TransportParameters transportData)
    {
        var candidates = new List<string>();

        foreach (var iceCandidate in transportData.Ice.Candidates)
        {
            var value = iceCandidate.Candidate?.Trim();

            if (string.IsNullOrEmpty(value))
                continue;

            if (value.StartsWith("a=", StringComparison.Ordinal))
                value = value[2..];

            if (!value.StartsWith("candidate:", StringComparison.Ordinal))
                value = $"candidate:{value}";

            // The SFU may report candidates as structured objects rather than attribute lines.
            // Anything that is not a well formed line is dropped here, because webrtcbin would
            // otherwise silently ignore the whole m-section and ICE would never start.
            if (!IsWellFormedCandidate(value))
                continue;

            candidates.Add(value);
        }

        if (candidates.Count == 0)
        {
            candidates.Add($"candidate:1 1 UDP 2130706431 {transportData.Host} {transportData.Port} typ host");
        }

        return candidates;
    }
}
