using Shared.Models.DataTransferObjects;
using Client.Core.Coordinator;

namespace Client.Api.Mappers;

internal static class CoordinatorSettingsMapper
{
    public static CoordinatorSettingsModel ToModel(this CoordinatorSettingsDTO dto) => new()
    {
        Revision = dto.Revision,
        Name = dto.Name,
        Ip = dto.Ip,
        Port = dto.Port,
        IsPasswordProtected = dto.IsPasswordProtected,
        MaxOnlineUsers = dto.MaxOnlineUsers,
        MaxUsers = dto.MaxUsers,
        IsMotdEnabled = dto.IsMotdEnabled,
        MotdText = dto.MotdText,
        AllowedIps = dto.AllowedIps,
        BannedIps = dto.BannedIps,
        HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
        HeartbeatTimeoutSeconds = dto.HeartbeatTimeoutSeconds,
        Stun = dto.Stun.ToModel(),
        Turn = dto.Turn.ToModel(),
        MaxFileSizeBytes = dto.MaxFileSizeBytes,
        StorageQuotaBytes = dto.StorageQuotaBytes,
        SfuNodeIds = dto.SfuNodeIds
    };

    public static SfuNodeModel ToModel(this SfuNodeDTO dto) => new(dto.Id, dto.Name, dto.Host, dto.Port, dto.IsEnabled,
        dto.ConnectionState is { } state
            ? new SfuNodeConnection(state, dto.PingMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
                dto.ConnectionError)
            : null);

    public static CoordinatorSettingsUpdateDTO ToDto(this CoordinatorSettingsUpdate update) => new()
    {
        Name = update.Name,
        Ip = update.Ip,
        Port = update.Port,
        NewPassword = update.NewPassword,
        RemovePassword = update.RemovePassword,
        MaxOnlineUsers = update.MaxOnlineUsers,
        MaxUsers = update.MaxUsers,
        IsMotdEnabled = update.IsMotdEnabled,
        MotdText = update.MotdText,
        AllowedIps = update.AllowedIps.ToList(),
        BannedIps = update.BannedIps.ToList(),
        HeartbeatIntervalSeconds = update.HeartbeatIntervalSeconds,
        HeartbeatTimeoutSeconds = update.HeartbeatTimeoutSeconds,
        Stun = update.Stun.ToDto(),
        Turn = update.Turn.ToDto(),
        MaxFileSizeBytes = update.MaxFileSizeBytes,
        StorageQuotaBytes = update.StorageQuotaBytes,
        SfuNodeIds = update.SfuNodeIds.ToList()
    };

    private static IceServerSettings ToModel(this IceServerSettingsDTO dto) =>
        new(dto.IsEnabled, dto.Address, dto.Port, dto.Username, dto.Password);

    private static IceServerSettingsDTO ToDto(this IceServerSettings settings) => new()
    {
        IsEnabled = settings.IsEnabled,
        Address = settings.Address,
        Port = settings.Port,
        Username = settings.Username,
        Password = settings.Password
    };
}
