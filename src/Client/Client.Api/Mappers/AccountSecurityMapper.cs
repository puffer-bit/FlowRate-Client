using Shared.Enums.Auth;
using Shared.Models.DataTransferObjects;
using Client.Core.Users;

namespace Client.Api.Mappers;

internal static class AccountSecurityMapper
{
    public static AccountSecurity ToModel(this UserSecurityDTO dto) => new(
        dto.HasPassword,
        dto.IsTwoFactorEnabled,
        dto.IsTwoFactorPending,
        dto.PasskeysAvailable,
        dto.Passkeys.Select(passkey => new AccountPasskey(passkey.Id, passkey.Label, passkey.CreatedAtUtc,
            passkey.LastUsedAtUtc)).ToList());

    public static UserSecurityProofDTO ToDto(this AccountProof proof) => new(
        string.IsNullOrEmpty(proof.Password) ? null : proof.Password,
        string.IsNullOrEmpty(proof.TwoFactorCode) ? null : proof.TwoFactorCode);

    /// <summary>
    /// Reads what the user can prove from the message of a first login stage: names of
    /// <see cref="SecondStageAuthType"/>, comma separated.
    /// </summary>
    public static LoginChallenge ToLoginChallenge(IReadOnlyCollection<SecondStageAuthType>? secondStages,
        FIDO2LoginOptionsDTO? passkeyOptions)
    {
        var methods = secondStages ?? [];

        return new LoginChallenge(methods.Contains(SecondStageAuthType.Password),
            methods.Contains(SecondStageAuthType.TwoFactor),
            methods.Contains(SecondStageAuthType.FIDO2) || passkeyOptions != null);
    }
}
