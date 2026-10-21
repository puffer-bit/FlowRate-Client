using Shared.Models.DataTransferObjects;
using Shared.Models.DataTransferObjects.ChatSession;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;

namespace Client.Api.Mappers;

internal static class SessionMapper
{
    public static SessionLayout ToModel(this SessionLayoutDTO dto) => new(
        dto.Revision,
        dto.RootSessionIds.ToList(),
        dto.Categories.Select(category => new SessionCategory(category.Id, category.Name, category.SessionIds.ToList()))
            .ToList());

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
            IsEntered = dto.IsEntered
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
            
            IsAudioRequested = dto.IsAudioRequested,
            IsApprovalRequested = dto.IsHostMustApprove,
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

}
