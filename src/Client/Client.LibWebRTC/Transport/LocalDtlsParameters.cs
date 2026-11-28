using Client.Core.Sessions.Hybrid;

namespace Client.LibWebRTC.Transport;

/// <summary>
/// Our certificate fingerprint and DTLS role from the answer of libwebrtc; the SFU checks the peer
/// certificate against exactly this.
/// </summary>
internal static class LocalDtlsParameters
{
    /// <returns>Null where the answer carries no fingerprint.</returns>
    public static DtlsParameters? Parse(string localSdp)
    {
        string? algorithm = null;
        string? fingerprint = null;
        var setup = "active";

        foreach (var rawLine in localSdp.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase) && fingerprint == null)
            {
                var value = line["a=fingerprint:".Length..].Split(' ', 2);

                if (value.Length == 2)
                {
                    algorithm = value[0].Trim();
                    fingerprint = value[1].Trim();
                }
            }
            else if (line.StartsWith("a=setup:", StringComparison.OrdinalIgnoreCase))
            {
                setup = line["a=setup:".Length..].Trim();
            }
        }

        if (algorithm == null || fingerprint == null)
            return null;

        // setup:active means we open the DTLS connection, which is the client role for the SFU
        var role = setup switch
        {
            "active" => "client",
            "passive" => "server",
            _ => "auto"
        };

        return new DtlsParameters(role, [new DtlsFingerprint(algorithm, fingerprint)]);
    }
}
