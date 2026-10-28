using Client.Api.Mappers;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Responses.Coordinator.Security;
using Client.Core.Users;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    public Task<AccountSecurity> GetAccountSecurity() =>
        SendSecurityRequestAsync(new UserSecurityInfoRequest());

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
