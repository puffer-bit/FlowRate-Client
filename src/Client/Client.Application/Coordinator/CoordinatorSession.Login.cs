using Client.Core.Coordinator;
using Shared.Enums;
using Shared.Enums.Auth;
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

    public Task<RequestResult<AccountSecurity>> GetAccountSecurity() =>
        SendAsync(_gateway.GetAccountSecurity);

    public async Task<RequestResult<AccountSecurity>> SetAccountPassword(AccountProof proof,
        string? newPassword)
    {
        var response = await SendAsync(() => _gateway.SetAccountPassword(proof, newPassword));

        // The next reconnect logs in with the new one, and a remembered password is replaced
        if (response.IsSuccess)
            KeepAccountPassword(newPassword, _isAccountPasswordRemembered && newPassword != null);

        return response;
    }

    public Task<RequestResult<TwoFactorSetup>> SetupTwoFactor(AccountProof proof) =>
        SendAsync(() => _gateway.SetupTwoFactor(proof));

    public Task<RequestResult<AccountSecurity>> EnableTwoFactor(string code) =>
        SendAsync(() => _gateway.EnableTwoFactor(code));

    public Task<RequestResult<AccountSecurity>> DisableTwoFactor(AccountProof proof) =>
        SendAsync(() => _gateway.DisableTwoFactor(proof));

    public Task<RequestResult<AccountSecurity>> DeletePasskey(AccountProof proof,
        int passkeyId) =>
        SendAsync(() => _gateway.DeletePasskey(proof, passkeyId));

    /// <summary>
    /// Finishes a login the server answered with <see cref="AuthResult.SecondStageRequired"/>. A known password
    /// goes without asking; a code, a password not known yet or one that was refused is asked of the user.
    /// </summary>
    /// <returns>The answer of the server, or null when the user gave the login up or cannot finish it here.</returns>
    /// <exception cref="RequestFailedException">The server refused the last answer, or failed otherwise.</exception>
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
        for (var tries = 1; ; tries++)
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

            try
            {
                var response = await _gateway.FinishLoginAsync(password, code).WaitAsync(cancellationToken);
                KeepAccountPassword(challenge.AsksPassword ? password : null, remember);
                return response;
            }
            catch (RequestFailedException exception) when (exception.Code == ErrorCode.AuthFailed
                                                           && tries < SecondStageTries)
            {
                // Which of the two was wrong is not told: the known password is asked for as well from now on
                LogSecondStageRefused(LogName, tries);
                knownPassword = null;
                isRetry = true;
            }
        }
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
