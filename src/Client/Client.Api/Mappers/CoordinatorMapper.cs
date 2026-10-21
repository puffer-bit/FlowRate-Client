using Shared.Models.DataTransferObjects;
using Client.Core.Coordinator;

namespace Client.Api.Mappers;

internal static class CoordinatorMapper
{
    public static CoordinatorSessionModel ToModel(this CoordinatorSessionDTO dto)
    {
        return new CoordinatorSessionModel
        {
            Id = dto.Id,
            Name = dto.Name,
            Address = dto.Address,
            Port = dto.Port,

            IsStunServerAvailable = dto.IsStunServerAvailable,
            IsTurnServerAvailable = dto.IsTurnServerAvailable,

            IsTurnEnabled = dto.IsTurnEnabled,
            TurnAddress = dto.TurnAddress,
            TurnPort = dto.TurnPort,
            TurnUsername = dto.TurnUsername,
            TurnPassword = dto.TurnPassword,

            IsStunEnabled = dto.IsStunEnabled,
            StunAddress = dto.StunAddress,
            StunPort = dto.StunPort,

            ConnectedUsersCount = dto.ConnectedUsersCount,

            ServerVersion = dto.ServerVersion,
            ServerPlatform = dto.ServerPlatform,

            CreateTime = dto.CreateTime,
            FirstLaunchTime = dto.FirstLaunchTime,
            CurrentLaunchTime = dto.CurrentLaunchTime,
            MaxOnlineUsers = dto.MaxOnlineUsers,
            MaxUsers = dto.MaxUsers,

            IsMOTDEnabled = dto.IsMOTDEnabled,
            SettingsRevision = dto.SettingsRevision
        };
    }
}
