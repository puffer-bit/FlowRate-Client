using Client.Api.Mappers;
using Shared.Enums.User;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Responses.Coordinator.Security;
using Client.Core.Users;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    public Task<(UserSecurityResult Result, AccountSecurity? Security)> GetAccountSecurity() =>
        SendSecurityRequestAsync(new UserSecurityInfoRequest());

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> SetAccountPassword(AccountProof proof,
        string? newPassword) =>
        SendSecurityRequestAsync(new UserSetPasswordRequest(proof.ToDto(), newPassword));

    public async Task<(UserSecurityResult Result, TwoFactorSetup? Setup)> SetupTwoFactor(AccountProof proof)
    {
        var response = await SendRequestAsync<UserTwoFactorSetupResponse>(new UserTwoFactorSetupRequest(proof.ToDto()));

        return response is { Result: UserSecurityResult.NoError, Secret: { } secret, Uri: { } uri }
            ? (response.Result, new TwoFactorSetup(secret, uri))
            : (response.Result == UserSecurityResult.NoError ? UserSecurityResult.InternalError : response.Result, null);
    }

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> EnableTwoFactor(string code) =>
        SendSecurityRequestAsync(new UserTwoFactorEnableRequest(code));

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> DisableTwoFactor(AccountProof proof) =>
        SendSecurityRequestAsync(new UserTwoFactorDisableRequest(proof.ToDto()));

    public Task<(UserSecurityResult Result, AccountSecurity? Security)> DeletePasskey(AccountProof proof,
        int passkeyId) =>
        SendSecurityRequestAsync(new UserPasskeyDeleteRequest(proof.ToDto(), passkeyId));

    private async Task<(UserSecurityResult Result, AccountSecurity? Security)> SendSecurityRequestAsync(
        Shared.Models.Requests.Request request)
    {
        var response = await SendRequestAsync<UserSecurityResponse>(request);

        return (response.Result, response.Security?.ToModel());
    }
}
