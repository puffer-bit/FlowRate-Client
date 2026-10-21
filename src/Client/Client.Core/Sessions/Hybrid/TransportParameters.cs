namespace Client.Core.Sessions.Hybrid;

/// <summary>
/// Remote side of the WebRTC transport of a hybrid session, as the SFU hands it out on join.
/// </summary>
public sealed record TransportParameters(
    string Host,
    ushort Port,
    DtlsParameters Dtls,
    IceParameters Ice);

public sealed record DtlsParameters(string Role, IReadOnlyList<DtlsFingerprint> Fingerprints);

public sealed record DtlsFingerprint(string Algorithm, string Value);

public sealed record IceParameters(string UsernameFragment, string Password, IReadOnlyList<IceCandidate> Candidates);

public sealed record IceCandidate(string Candidate, string SdpMid, int SdpMLineIndex);
