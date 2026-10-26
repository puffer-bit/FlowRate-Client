using Shared.Enums.Auth;
using Shared.Enums.User;
using Client.Core.Users;
using Client.Application.Ports;

namespace Client.Application.Coordinator;

// The password of the account is kept here for the life of the session, so that a reconnect does not ask for it
// again; a one-time code is asked for every login. The password is written by the attempts and by the changes of
// the account, which do not run at the same time: a change needs the connection an attempt is making.
public partial class CoordinatorSession
{
    // The server closes the connection after so many wrong answers
    private const int SecondStageTries = 3;

    private volatile string? _accountPassword;
    private volatile bool _isAccountPasswordRemembered;
    private string? _accountPasswordToSave;

    public LoginPromptHandler? LoginPrompt { get; set; }

    public event Action<string?>? AccountPasswordToSaveChanged;

    public string? AccountPasswordToSave => _isAccountPasswordRemembered ? _accountPassword : null;

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> GetAccountSecurity() =>
        SendAsync(_gateway.GetAccountSecurity, UserSecurityResult.InternalError, UserSecurityResult.InternalError);

    public async Task<(UserSecurityResult Result, AccountSecurity? Security)> SetAccountPassword(AccountProof proof,
        string? newPassword)
    {
        var response = await SendAsync(() => _gateway.SetAccountPassword(proof, newPassword),
            UserSecurityResult.InternalError, UserSecurityResult.InternalError);

        // The next reconnect logs in with the new one, and a remembered password is replaced
        if (response.Result == UserSecurityResult.NoError)
            KeepAccountPassword(newPassword, _isAccountPasswordRemembered && newPassword != null);

        return response;
    }

    public Task<(UserSecurityResult Result, TwoFactorSetup? Setup)> SetupTwoFactor(AccountProof proof) =>
        SendAsync(() => _gateway.SetupTwoFactor(proof), UserSecurityResult.InternalError,
            UserSecurityResult.InternalError);

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> EnableTwoFactor(string code) =>
        SendAsync(() => _gateway.EnableTwoFactor(code), UserSecurityResult.InternalError,
            UserSecurityResult.InternalError);

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> DisableTwoFactor(AccountProof proof) =>
        SendAsync(() => _gateway.DisableTwoFactor(proof), UserSecurityResult.InternalError,
            UserSecurityResult.InternalError);

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> DeletePasskey(AccountProof proof,
        int passkeyId) =>
        SendAsync(() => _gateway.DeletePasskey(proof, passkeyId), UserSecurityResult.InternalError,
            UserSecurityResult.InternalError);

    /// <summary>
    /// Finishes a login the server answered with <see cref="AuthResult.SecondStageRequeried"/>. A known password
    /// goes without asking; a code, a password not known yet or one that was refused is asked of the user.
    /// </summary>
    /// <returns>The last answer of the server, or null when the user gave the login up or cannot finish it here.</returns>
    private async Task<AuthenticationResult?> PassSecondStageAsync(string address, int port, LoginChallenge challenge,
        CancellationToken cancellationToken)
    {
        // Passkeys are made and used in the control panel only
        if (!challenge.AcceptsCodes)
        {
            LogPasskeyLoginUnsupported(LogName);
            return null;
        }

        var knownPassword = challenge.AsksPassword ? _accountPassword : null;
        var isRetry = false;
        AuthenticationResult? response = null;

        for (var tries = 0; tries < SecondStageTries; tries++)
        {
            var password = knownPassword;
            string? code = null;
            var remember = _isAccountPasswordRemembered;
            var asksPassword = challenge.AsksPassword && knownPassword == null;

            if (asksPassword || challenge.AsksTwoFactorCode)
            {
                if (LoginPrompt is not { } prompt)
                    return null;

                var answer = await prompt(new LoginPrompt($"{address}:{port}", challenge, asksPassword,
                    challenge.AsksTwoFactorCode, isRetry, _isAccountPasswordRemembered), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (answer == null)
                {
                    LogSecondStageGivenUp(LogName);
                    return null;
                }

                if (asksPassword)
                {
                    password = answer.Password;
                    remember = answer.RememberPassword;
                }

                code = challenge.AsksTwoFactorCode ? answer.TwoFactorCode : null;
            }

            response = await _gateway.FinishLoginAsync(password, code).WaitAsync(cancellationToken);

            if (response.Result == AuthResult.Success)
            {
                KeepAccountPassword(challenge.AsksPassword ? password : null, remember);
                return response;
            }

            if (response.Result != AuthResult.AuthFailed)
                return response;

            // Which of the two was wrong is not told: the known password is asked for as well from now on
            LogSecondStageRefused(LogName, tries + 1);
            knownPassword = null;
            isRetry = true;
        }

        return response;
    }

    private void KeepAccountPassword(string? password, bool remember)
    {
        _accountPassword = password;
        _isAccountPasswordRemembered = remember && password != null;

        var toSave = AccountPasswordToSave;
        if (toSave == _accountPasswordToSave)
            return;

        _accountPasswordToSave = toSave;
        AccountPasswordToSaveChanged?.Invoke(toSave);
    }
}
