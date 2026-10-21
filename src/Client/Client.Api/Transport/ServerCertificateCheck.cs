using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Client.Api.Transport;

/// <summary>
/// The rule every connection to a coordinator trusts its certificate by: the system verifies it,
/// or the user has trusted exactly this certificate.
/// </summary>
internal static class ServerCertificateCheck
{
    /// <param name="fingerprint">SHA-256 of the certificate, null when the server sent none.</param>
    public static bool IsTrusted(X509Certificate? certificate, SslPolicyErrors errors, string? trustedFingerprint,
        out string? fingerprint)
    {
        fingerprint = null;
        if (errors == SslPolicyErrors.None)
            return true;
        if (certificate == null)
            return false;

        fingerprint = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
        return string.Equals(fingerprint, trustedFingerprint, StringComparison.OrdinalIgnoreCase);
    }
}
