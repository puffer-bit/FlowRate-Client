using Client.Application.Ports;
using Client.Application.Security;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Client.Core.Users;
using Shared.Auth;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;

namespace Client.Application.Coordinator;

// How the session logs in. The plan of the first connect may add a device; the reconnects then log in as that device.
// The account password is asked for each login that needs it and kept for the life of the session only.
public partial class CoordinatorSession
{
    // The server closes the connection after so many wrong answers
    private const int SecondStageTries = 3;

    private readonly IDeviceKeyStore _deviceKeys;
    // The keys of temporary devices are never written anywhere
    private readonly InMemoryDeviceKeyStore _temporaryKeys = new();

    // Written by the attempts and the connects, which do not run at the same time
    private volatile LoginPlan? _loginPlan;
    private volatile string? _accountPassword;
    // The phrase of an account this session registered or moved over, until the caller takes it
    private volatile string? _newRecoveryPhrase;
    // The backup key of a login with the recovery phrase, which is not kept
    private volatile string? _backupKey;
    private TaskCompletionSource<CoordinatorEvent>? _deviceLinkWait;

    public LoginPromptHandler? LoginPrompt { get; set; }

    public event Action<DeviceEndReason?>? DeviceEnded;

    public event Action<AccountDevice>? DeviceAdded;

    public event Action<DeviceLinkCode>? DeviceLinkCodeReceived;

    public bool IsTemporaryDevice => _loginPlan?.IsTemporary == true;

    public Task<RequestResult<IReadOnlyList<AccountDevice>>> GetAccountDevices() =>
        SendAsync(_gateway.GetAccountDevices);

    public Task<RequestResult<IReadOnlyList<AccountDevice>>> RevokeAccountDevice(Guid? deviceId) =>
        SendAsync(() => _gateway.RevokeAccountDevice(deviceId));

    public Task<RequestResult<DeviceLinkInfo>> GetDeviceLinkInfo(string code) =>
        SendAsync(() => _gateway.GetDeviceLinkInfo(code));

    public Task<RequestResult> ApproveDeviceLink(string code, bool approve) =>
        SendAsync(() => _gateway.ApproveDeviceLink(code, approve));

    /// <summary>
    /// Replaces the recovery phrase of the account with a new one; answers with it. The old phrase stops working.
    /// </summary>
    public async Task<RequestResult<string>> ResetRecoveryPhrase(AccountProof proof)
    {
        var phrase = RecoveryPhrase.Generate();
        using var keys = RecoveryKeySet.FromPhrase(phrase)!;
        var result = await SendAsync(() => _gateway.ResetRecoveryKeys(proof, ToDto(keys)));
        return result.IsSuccess ? phrase : result;
    }

    /// <summary>
    /// Removes this device from the account and disconnects; the key is dropped. The saved account then needs a login.
    /// </summary>
    public async Task<RequestResult> Logout()
    {
        var result = await SendAsync(_gateway.LogoutAsync);
        if (result.IsSuccess)
            await ForgetDeviceKeyAsync();
        await Disconnect();
        return result;
    }

    public Task<RequestResult<AccountSecurity>> GetAccountSecurity() =>
        SendAsync(_gateway.GetAccountSecurity);

    public async Task<RequestResult<AccountSecurity>> SetAccountPassword(AccountProof proof,
        string? newPassword)
    {
        var response = await SendAsync(() => _gateway.SetAccountPassword(proof, newPassword));
        if (response.IsSuccess)
            _accountPassword = newPassword;
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

    /// <summary>The phrase of an account this session just registered or moved over; null once taken.</summary>
    public string? TakeNewRecoveryPhrase() => Interlocked.Exchange(ref _newRecoveryPhrase, null);

    private IDeviceKeyStore KeysOf(LoginPlan plan) => plan.IsTemporary ? _temporaryKeys : _deviceKeys;

    /// <summary>
    /// Logs the open connection in with the plan. A plan that adds a device leaves the session logging in as that
    /// device from then on.
    /// </summary>
    /// <returns>The answer of the server, or the state to end with when the login cannot be finished here.</returns>
    /// <exception cref="RequestFailedException">The server refused the login.</exception>
    private async Task<(AuthenticationResult? Result, CoordinatorState? Failure)> LogInAsync(string address, int port,
        string? serverPassword, CancellationToken cancellationToken)
    {
        var plan = _loginPlan ?? throw new InvalidOperationException("Connect was not called.");

        if (plan is DeviceLoginPlan device)
        {
            var (nonce, coordinatorId) = await _gateway.GetLoginChallengeAsync().WaitAsync(cancellationToken);
            var signature = await KeysOf(plan).SignAsync(device.KeyName,
                LoginSignature.Payload(LoginSignature.DevicePurpose, coordinatorId, nonce), cancellationToken);
            if (signature == null)
            {
                LogDeviceKeyMissing(LogName);
                OnDeviceEnded(null);
                return (null, CoordinatorState.DeviceEnded);
            }

            var result = await _gateway.LoginAsync(serverPassword,
                new DeviceLoginDTO(device.DeviceId, LoginSignature.Encode(signature))).WaitAsync(cancellationToken);
            return (result, null);
        }

        // Every other plan adds a device: its key is made first, and dropped again when the login fails
        var keyName = SecretNames.NewDeviceKey();
        var keys = KeysOf(plan);
        var publicKey = LoginSignature.Encode(await keys.CreateAsync(keyName, cancellationToken));
        var newDevice = new NewDeviceDTO(publicKey, plan.IsTemporary);
        try
        {
            var (result, failure) = await AddDeviceAsync(plan, address, port, serverPassword, newDevice,
                cancellationToken);
            if (result is { Result: AuthResult.Success, DeviceId: { } deviceId })
            {
                _loginPlan = new DeviceLoginPlan(deviceId, keyName, plan.IsTemporary);
                return (result, null);
            }

            await keys.DeleteAsync(keyName, CancellationToken.None);
            return (result, failure);
        }
        catch
        {
            await keys.DeleteAsync(keyName, CancellationToken.None);
            throw;
        }
    }

    private async Task<(AuthenticationResult? Result, CoordinatorState? Failure)> AddDeviceAsync(LoginPlan plan,
        string address, int port, string? serverPassword, NewDeviceDTO newDevice, CancellationToken cancellationToken)
    {
        switch (plan)
        {
            case RegisterLoginPlan:
            {
                var phrase = RecoveryPhrase.Generate();
                using var keys = RecoveryKeySet.FromPhrase(phrase)!;
                var result = await _gateway.LoginAsync(serverPassword, new RegisterLoginDTO(newDevice, ToDto(keys)))
                    .WaitAsync(cancellationToken);
                if (result.Result == AuthResult.Success)
                    _newRecoveryPhrase = phrase;
                return (result, null);
            }

            case RecoveryLoginPlan recovery:
            {
                using var keys = RecoveryKeySet.FromPhrase(recovery.Phrase);
                if (keys == null)
                    return (null, CoordinatorState.LoginRefused);

                var (nonce, coordinatorId) = await _gateway.GetLoginChallengeAsync().WaitAsync(cancellationToken);
                var signature = VaultDeviceKeyStore.Sign(keys.RecoveryKey, LoginSignature.Payload(
                    LoginSignature.RecoveryPurpose, coordinatorId, nonce, newDevice.PublicKey));
                var result = await _gateway.LoginAsync(serverPassword, new RecoveryLoginDTO(newDevice,
                        LoginSignature.Encode(keys.RecoveryPublicKey), LoginSignature.Encode(signature)))
                    .WaitAsync(cancellationToken);
                if (result.Result == AuthResult.Success)
                    _backupKey = Convert.ToBase64String(keys.BackupKey);
                return (result, null);
            }

            case UsernameLoginPlan username:
                return await PassSecondStageIfAskedAsync(address, port,
                    await _gateway.LoginAsync(serverPassword, new UsernameLoginDTO(newDevice, username.Username.Trim()))
                        .WaitAsync(cancellationToken), cancellationToken);

            case LoginCodeLoginPlan code:
                return (await _gateway.LoginAsync(serverPassword, new LoginCodeLoginDTO(newDevice, code.Code.Trim()))
                    .WaitAsync(cancellationToken), null);

            case LegacyLoginPlan legacy:
            {
                var phrase = RecoveryPhrase.Generate();
                using var keys = RecoveryKeySet.FromPhrase(phrase)!;
                _accountPassword ??= legacy.AccountPassword;
                var (result, failure) = await PassSecondStageIfAskedAsync(address, port,
                    await _gateway.LoginAsync(serverPassword, new LegacyLoginDTO(newDevice, legacy.Identity,
                        legacy.SessionToken, ToDto(keys))).WaitAsync(cancellationToken), cancellationToken);
                if (result?.Result == AuthResult.Success)
                {
                    _newRecoveryPhrase = phrase;
                    LogMovedOverToDevice(LogName);
                }
                return (result, failure);
            }

            case DeviceLinkLoginPlan:
                return await WaitForDeviceLinkAsync(serverPassword, newDevice, cancellationToken);

            default:
                throw new InvalidOperationException($"Unknown login plan {plan.GetType().Name}.");
        }
    }

    private async Task<(AuthenticationResult? Result, CoordinatorState? Failure)> WaitForDeviceLinkAsync(
        string? serverPassword, NewDeviceDTO newDevice, CancellationToken cancellationToken)
    {
        var wait = new TaskCompletionSource<CoordinatorEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        _deviceLinkWait = wait;
        try
        {
            var code = await _gateway.StartDeviceLinkAsync(serverPassword, newDevice).WaitAsync(cancellationToken);
            DeviceLinkCodeReceived?.Invoke(code);

            var remaining = code.ExpiresAt - DateTimeOffset.UtcNow;
            var outcome = await wait.Task.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                cancellationToken);
            return outcome is DeviceLinkCompletedEvent completed
                ? (new AuthenticationResult(AuthResult.Success, completed.User, completed.DeviceId), null)
                : (null, CoordinatorState.DeviceLinkDeclined);
        }
        catch (TimeoutException)
        {
            return (null, CoordinatorState.DeviceLinkDeclined);
        }
        finally
        {
            _deviceLinkWait = null;
        }
    }

    private void OnDeviceLinkEvent(CoordinatorEvent linkEvent) => _deviceLinkWait?.TrySetResult(linkEvent);

    /// <summary>
    /// Finishes a login the server answered with <see cref="AuthResult.SecondStageRequired"/>. A password known from
    /// this session goes without asking; a code, a password not known yet or one that was refused is asked of the user.
    /// </summary>
    private async Task<(AuthenticationResult? Result, CoordinatorState? Failure)> PassSecondStageIfAskedAsync(
        string address, int port, AuthenticationResult response, CancellationToken cancellationToken)
    {
        if (response is not { Result: AuthResult.SecondStageRequired, Challenge: { } challenge })
            return (response, null);

        // Passkeys are made and used in the control panel only
        if (!challenge.AcceptsCodes)
        {
            LogPasskeyLoginUnsupported(LogName);
            return (null, CoordinatorState.PasskeyRequired);
        }

        var knownPassword = challenge.AsksPassword ? _accountPassword : null;
        var isRetry = false;
        for (var tries = 1; ; tries++)
        {
            var password = knownPassword;
            string? code = null;
            var asksPassword = challenge.AsksPassword && knownPassword == null;

            if (asksPassword || challenge.AsksTwoFactorCode)
            {
                if (LoginPrompt is not { } prompt)
                    return (null, CoordinatorState.AuthenticationNeeded);

                var answer = await prompt(new LoginPrompt($"{address}:{port}", challenge, asksPassword,
                    challenge.AsksTwoFactorCode, isRetry), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (answer == null)
                {
                    LogSecondStageGivenUp(LogName);
                    return (null, CoordinatorState.AuthenticationNeeded);
                }

                if (asksPassword)
                    password = answer.Password;
                code = challenge.AsksTwoFactorCode ? answer.TwoFactorCode : null;
            }

            try
            {
                var finished = await _gateway.FinishLoginAsync(password, code).WaitAsync(cancellationToken);
                _accountPassword = challenge.AsksPassword ? password : null;
                return (finished, null);
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

    /// <summary>The device is not one of the account any more: its key goes, and the session stays offline.</summary>
    private void OnDeviceEnded(DeviceEndReason? reason)
    {
        _ = ForgetDeviceKeyAsync();
        DeviceEnded?.Invoke(reason);
    }

    private async Task ForgetDeviceKeyAsync()
    {
        if (_loginPlan is not DeviceLoginPlan device)
            return;

        _loginPlan = null;
        try
        {
            await KeysOf(device).DeleteAsync(device.KeyName);
        }
        catch (Exception exception)
        {
            LogDeviceKeyNotDeleted(LogName, exception.Message);
        }
    }

    private static RecoveryKeysDTO ToDto(RecoveryKeySet keys) =>
        new(LoginSignature.Encode(keys.RecoveryPublicKey), LoginSignature.Encode(keys.AccountPublicKey));
}
