using Client.Api.Mappers;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Coordinator.Devices;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Responses.Coordinator.Devices;
using Shared.Models.Responses.Coordinator.Security;
using Client.Core.Users;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    public Task<AccountSecurity> GetAccountSecurity() =>
        SendSecurityRequestAsync(new UserSecurityInfoRequest());

    public Task<IReadOnlyList<AccountDevice>> GetAccountDevices() =>
        SendDevicesRequestAsync(new AccountDevicesRequest());

    public Task<IReadOnlyList<AccountDevice>> RevokeAccountDevice(Guid? deviceId) =>
        SendDevicesRequestAsync(new AccountDeviceRevokeRequest(deviceId));

    public Task LogoutAsync() => SendRequestAsync<LogoutResponse>(new LogoutRequest());

    public async Task<DeviceLinkInfo> GetDeviceLinkInfo(string code)
    {
        var response = await SendRequestAsync<DeviceLinkInfoResponse>(new DeviceLinkInfoRequest(code.Trim()));
        return new DeviceLinkInfo(response.Device?.Name, response.Device?.Platform, response.Device?.AppVersion,
            response.Address);
    }

    public Task ApproveDeviceLink(string code, bool approve) =>
        SendRequestAsync<DeviceLinkApproveResponse>(new DeviceLinkApproveRequest(code.Trim(), approve));

    public Task ResetRecoveryKeys(AccountProof proof, RecoveryKeysDTO keys) =>
        SendRequestAsync<UserRecoveryKeyResetResponse>(new UserRecoveryKeyResetRequest(proof.ToDto(), keys));

    private async Task<IReadOnlyList<AccountDevice>> SendDevicesRequestAsync(Shared.Models.Requests.Request request) =>
        (await SendRequestAsync<AccountDevicesResponse>(request)).Devices.Select(device => device.ToModel()).ToList();

    public Task<AccountSecurity> SetAccountPassword(AccountProof proof, string? newPassword) =>
        SendSecurityRequestAsync(new UserSetPasswordRequest(proof.ToDto(), newPassword));

    public async Task<TwoFactorSetup> SetupTwoFactor(AccountProof proof)
    {
        var response = await SendRequestAsync<UserTwoFactorSetupResponse>(new UserTwoFactorSetupRequest(proof.ToDto()));

        return new TwoFactorSetup(response.Secret, response.Uri);
    }

    public Task<AccountSecurity> EnableTwoFactor(string code) =>
        SendSecurityRequestAsync(new UserTwoFactorEnableRequest(code));

    public Task<AccountSecurity> DisableTwoFactor(AccountProof proof) =>
        SendSecurityRequestAsync(new UserTwoFactorDisableRequest(proof.ToDto()));

    public Task<AccountSecurity> DeletePasskey(AccountProof proof, int passkeyId) =>
        SendSecurityRequestAsync(new UserPasskeyDeleteRequest(proof.ToDto(), passkeyId));

    private async Task<AccountSecurity> SendSecurityRequestAsync(Shared.Models.Requests.Request request) =>
        (await SendRequestAsync<UserSecurityResponse>(request)).Security.ToModel();
}
