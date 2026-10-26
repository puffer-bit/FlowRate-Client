using Shared.Models.DataTransferObjects;
using Shared.Models.DataTransferObjects.ChatSession;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Shared.Permissions;

namespace Client.Api.Mappers;

internal static class SessionMapper
{
    public static SessionLayout ToModel(this SessionLayoutDTO dto) => new(
        dto.Revision,
        dto.RootSessionIds.ToList(),
        dto.Categories.Select(category => new SessionCategory(category.Id, category.Name, category.SessionIds.ToList()))
            .ToList());

    public static IReadOnlyList<PermissionOverride> ToModel(this IEnumerable<SessionPermissionOverrideDTO> dtos) =>
        dtos.Select(dto => new PermissionOverride(dto.RoleId, dto.Allow, dto.Deny)).ToList();

    public static List<SessionPermissionOverrideDTO> ToDTO(this IEnumerable<PermissionOverride> overrides) =>
        overrides.Select(o => new SessionPermissionOverrideDTO { RoleId = o.RoleId, Allow = o.Allow, Deny = o.Deny })
            .ToList();

    public static SessionModel ToModel(this SessionDTO dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        return dto switch
        {
            HybridSessionDTO hybrid => hybrid.ToModel(),
            ChatSessionDTO chat => chat.ToModel(),
            _ => dto.ToBaseModel()
        };
    }

    public static SessionModel ToBaseModel(this SessionDTO dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        return new SessionModel
        {
            Id = dto.Id,
            Name = dto.Name,
            Capacity = dto.Capacity,
            SessionType = dto.SessionType,
            IsEntered = dto.IsEntered,
            PermissionOverrides = dto.PermissionOverrides.ToModel()
        };
    }

    public static HybridSessionModel ToModel(this HybridSessionDTO dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));
        
        return new HybridSessionModel
        {
            Id = dto.Id,
            Name = dto.Name,
            EngineType = dto.EngineType,
            HostId = dto.HostId,
            Capacity = dto.Capacity,
            SessionType = dto.SessionType,
            IsEntered = dto.IsEntered,
            PermissionOverrides = dto.PermissionOverrides.ToModel(),
            
            IsAudioRequested = dto.IsAudioRequested,
            IsApprovalRequested = dto.IsHostMustApprove,
            Media = dto.Media.ToModel(),
        };
    }
    
    public static ChatSessionModel ToModel(this ChatSessionDTO sessionDTO)
    {
        return new ChatSessionModel
        {
            Id = sessionDTO.Id,
            Name = sessionDTO.Name,
            Capacity = sessionDTO.Capacity,
            SessionType = sessionDTO.SessionType,   
            PermissionOverrides = sessionDTO.PermissionOverrides.ToModel(),

            CreatorUserId = sessionDTO.CreatorUserId,
            AdminUserId = sessionDTO.AdminUserId,
            Description = sessionDTO.Description,
            IconPath = sessionDTO.IconPath,

            IsReadOnly = sessionDTO.IsReadOnly,
            IsArchived = sessionDTO.IsArchived,
            IsAttachmentsAllowed = sessionDTO.IsAttachmentsAllowed,
            IsVoiceMessagesAllowed = sessionDTO.IsVoiceMessagesAllowed,
            IsVideoMessagesAllowed = sessionDTO.IsVideoMessagesAllowed,
            IsDelayedMessagesAllowed = sessionDTO.IsDelayedMessagesAllowed,
            
        };
    }

    public static MediaSettings ToModel(this HybridMediaSettingsDTO dto) => new(
        dto.CameraCodec,
        dto.ScreenCodec,
        dto.AudioCodec,
        dto.IsVideoNackEnabled,
        dto.IsVideoRtxEnabled,
        dto.KeyFrameRequestDelayMs,
        dto.IsAudioNackEnabled,
        dto.IsOpusFecEnabled,
        dto.OpusPacketLossPercent,
        dto.IsVoiceDtxEnabled,
        dto.VoiceJitterBufferMs,
        dto.VideoJitterBufferMs);

    public static HybridMediaSettingsDTO ToDto(this MediaSettings media) => new()
    {
        CameraCodec = media.CameraCodec,
        ScreenCodec = media.ScreenCodec,
        AudioCodec = media.AudioCodec,
        IsVideoNackEnabled = media.IsVideoNackEnabled,
        IsVideoRtxEnabled = media.IsVideoRtxEnabled,
        KeyFrameRequestDelayMs = media.KeyFrameRequestDelayMs,
        IsAudioNackEnabled = media.IsAudioNackEnabled,
        IsOpusFecEnabled = media.IsOpusFecEnabled,
        OpusPacketLossPercent = media.OpusPacketLossPercent,
        IsVoiceDtxEnabled = media.IsVoiceDtxEnabled,
        VoiceJitterBufferMs = media.VoiceJitterBufferMs,
        VideoJitterBufferMs = media.VideoJitterBufferMs
    };
}
