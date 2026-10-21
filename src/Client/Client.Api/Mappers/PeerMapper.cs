using Shared.Models.DataTransferObjects;
using Client.Core.Sessions.Hybrid;

namespace Client.Api.Mappers;

internal static class PeerMapper
{
    public static PeerModel ToModel(this PeerDTO dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        return new PeerModel
        {
            UserId = dto.UserId,
            IsAudioRequested = dto.IsAudioRequested,
            State = dto.State,
            IsNegotiated = dto.IsNegotiated,
            IsConnected = dto.IsConnected,
            IsApproved = dto.IsApproved,
            IsRejected = dto.IsRejected,
            IsMicrophoneMuted = dto.IsMicrophoneMuted,
            IsSoundMuted = dto.IsSoundMuted,
            IsScreenShareActive = dto.IsScreenShareActive,
            IsScreenAudioActive = dto.IsScreenAudioActive
        };
    }
}
