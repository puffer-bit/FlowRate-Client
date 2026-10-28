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
        IsMotdEnabled = dto.IsMOTDEnabled,
        MotdText = dto.MOTDText,
        AllowedIps = dto.AllowedIps,
        BannedIps = dto.BannedIps,
        HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
        HeartbeatTimeoutSeconds = dto.HeartbeatTimeoutSeconds,
        MaxFileSizeBytes = dto.MaxFileSizeBytes,
        StorageQuotaBytes = dto.StorageQuotaBytes,
        MaxUnattachedUploadsPerUser = dto.MaxUnattachedUploadsPerUser,
        UnattachedUploadLifetimeHours = dto.UnattachedUploadLifetimeHours,
        MediaTokenLifetimeMinutes = dto.MediaTokenLifetimeMinutes,
        RateLimits = dto.RateLimits.ToModel(),
        SfuNodeIds = dto.SFUNodeIds
    };

    public static SfuNodeModel ToModel(this SFUNodeDTO dto) => new(dto.Id, dto.Name, dto.Host, dto.Port, dto.IsEnabled,
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
        IsMOTDEnabled = update.IsMotdEnabled,
        MOTDText = update.MotdText,
        AllowedIps = update.AllowedIps.ToList(),
        BannedIps = update.BannedIps.ToList(),
        HeartbeatIntervalSeconds = update.HeartbeatIntervalSeconds,
        HeartbeatTimeoutSeconds = update.HeartbeatTimeoutSeconds,
        MaxFileSizeBytes = update.MaxFileSizeBytes,
        StorageQuotaBytes = update.StorageQuotaBytes,
        MaxUnattachedUploadsPerUser = update.MaxUnattachedUploadsPerUser,
        UnattachedUploadLifetimeHours = update.UnattachedUploadLifetimeHours,
        MediaTokenLifetimeMinutes = update.MediaTokenLifetimeMinutes,
        RateLimits = update.RateLimits.ToDto(),
        SFUNodeIds = update.SfuNodeIds.ToList()
    };

    private static CoordinatorRateLimits ToModel(this CoordinatorRateLimitsDTO dto) => new()
    {
        Global = dto.Global.ToModel(),
        AuthenticationPerIp = dto.AuthenticationPerIp.ToModel(),
        SessionManagement = dto.SessionManagement.ToModel(),
        SessionLayout = dto.SessionLayout.ToModel(),
        SessionMedia = dto.SessionMedia.ToModel(),
        MicrophoneMute = dto.MicrophoneMute.ToModel(),
        InboundResume = dto.InboundResume.ToModel(),
        Moderation = dto.Moderation.ToModel(),
        ChatWrite = dto.ChatWrite.ToModel(),
        ChatRead = dto.ChatRead.ToModel(),
        PushRegistration = dto.PushRegistration.ToModel(),
        MediaToken = dto.MediaToken.ToModel(),
        Query = dto.Query.ToModel(),
        Heartbeat = dto.Heartbeat.ToModel(),
        MaxRejectionsPerWindow = dto.MaxRejectionsPerWindow,
        RejectionWindowSeconds = dto.RejectionWindowSeconds,
        MaxConnectionsPerIp = dto.MaxConnectionsPerIp
    };

    private static CoordinatorRateLimitsDTO ToDto(this CoordinatorRateLimits limits) => new()
    {
        Global = limits.Global.ToDto(),
        AuthenticationPerIp = limits.AuthenticationPerIp.ToDto(),
        SessionManagement = limits.SessionManagement.ToDto(),
        SessionLayout = limits.SessionLayout.ToDto(),
        SessionMedia = limits.SessionMedia.ToDto(),
        MicrophoneMute = limits.MicrophoneMute.ToDto(),
        InboundResume = limits.InboundResume.ToDto(),
        Moderation = limits.Moderation.ToDto(),
        ChatWrite = limits.ChatWrite.ToDto(),
        ChatRead = limits.ChatRead.ToDto(),
        PushRegistration = limits.PushRegistration.ToDto(),
        MediaToken = limits.MediaToken.ToDto(),
        Query = limits.Query.ToDto(),
        Heartbeat = limits.Heartbeat.ToDto(),
        MaxRejectionsPerWindow = limits.MaxRejectionsPerWindow,
        RejectionWindowSeconds = limits.RejectionWindowSeconds,
        MaxConnectionsPerIp = limits.MaxConnectionsPerIp
    };

    private static RequestRate ToModel(this RequestRateDTO dto) => new(dto.Count, dto.PeriodSeconds);

    private static RequestRateDTO ToDto(this RequestRate rate) => new()
    {
        Count = rate.Count,
        PeriodSeconds = rate.PeriodSeconds
    };
}
