using System;
using Client.Avalonia.Localization;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.ServerSettings;

public sealed class BanRowVM(BanDTO ban)
{
    public BanDTO Ban { get; } = ban;
    public string Target => ban.Kind == BanKind.Ip ? $"IP: {ban.Address}" : $"{Strings.Get("ban.kind.account")}: #{ban.UserId}";
    public string Reason => ban.Reason;
    public bool IsActive => ban.RevokedAt == null && (ban.ExpiresAt == null || ban.ExpiresAt > DateTime.UtcNow);
    public string Details => $"{ban.CreatedAt.ToLocalTime():g} · #{ban.CreatedBy} · " +
        (ban.RevokedAt != null ? $"{Strings.Get("ban.revoked")} {ban.RevokedAt.Value.ToLocalTime():g} · #{ban.RevokedBy}" :
        ban.ExpiresAt != null ? $"{Strings.Get("ban.expires")} {ban.ExpiresAt.Value.ToLocalTime():g}" : Strings.Get("ban.duration.permanent"));
}
