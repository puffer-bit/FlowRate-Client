using Client.Application.Coordinator;
using Client.Core.Coordinator;

namespace Client.TestBot.Accounts;

internal static class BotLogin
{
    /// <summary>
    /// Connects with the plan, trusting the certificate of the server the first time as a user who checked it would.
    /// </summary>
    /// <returns>The state the connect ended in.</returns>
    public static async Task<CoordinatorState> ConnectAsync(ICoordinatorSession session, BotOptions options,
        LoginPlan login, Action<string> log)
    {
        await session.Connect(options.Host, options.Port, options.Password, login);

        if (session is { ConnectionStatus: CoordinatorState.CertificateNotTrusted, UntrustedCertificate: { } untrusted })
        {
            log($"trusting certificate {untrusted.Certificate.Fingerprint}");
            await session.TrustCertificateAsync(untrusted);
            await session.Connect(options.Host, options.Port, options.Password, login);
        }

        return session.ConnectionStatus;
    }

    /// <summary>The device the session logged in as, to log in as it the next time.</summary>
    public static SavedDevice? DeviceOf(ICoordinatorSession session) =>
        session.GetCurrentUserCredentials() is { DeviceId: { } deviceId, DeviceKey: { } keyName, Id: var userId }
            ? new SavedDevice(deviceId, keyName, userId)
            : null;
}
