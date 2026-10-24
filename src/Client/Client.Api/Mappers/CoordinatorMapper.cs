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
