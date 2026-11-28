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
        dto.Passkeys.Select(passkey => new AccountPasskey(passkey.Id, passkey.Label, passkey.CreatedAt,
            passkey.LastUsedAt)).ToList());

    public static AccountDevice ToModel(this AccountDeviceDTO dto) => new(dto.Id, dto.Device.Name, dto.Device.Platform,
        dto.Device.AppVersion, dto.CreatedAt, dto.LastUsedAt, dto.ExpiresAt, dto.LastAddress, dto.IsCurrent,
        dto.IsOnline, dto.IsTemporary, dto.AddedBy);

    public static UserSecurityProofDTO ToDto(this AccountProof proof) => new(
        string.IsNullOrEmpty(proof.Password) ? null : proof.Password,
        string.IsNullOrEmpty(proof.TwoFactorCode) ? null : proof.TwoFactorCode);

    /// <summary>What the user can prove in the second stage of a login by username.</summary>
    public static LoginChallenge ToLoginChallenge(IReadOnlyCollection<LoginFactor>? factors,
        FIDO2LoginOptionsDTO? passkeyOptions)
    {
        var methods = factors ?? [];

        return new LoginChallenge(methods.Contains(LoginFactor.Password),
            methods.Contains(LoginFactor.TwoFactor),
            methods.Contains(LoginFactor.Passkey) || passkeyOptions != null);
    }
}
