using Shared.Models.DataTransferObjects;
using Client.Core.Chat;
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

            CreateTime = dto.CreatedAt,
            FirstLaunchTime = dto.FirstLaunchedAt,
            CurrentLaunchTime = dto.LaunchedAt,
            MaxOnlineUsers = dto.MaxOnlineUsers,
            MaxUsers = dto.MaxUsers,

            IsMOTDEnabled = dto.IsMOTDEnabled,
            SettingsRevision = dto.SettingsRevision,
            ImagePolicy = ImageCompressionPolicy.From(dto.ImageMaxSide, dto.ImageQuality, dto.AllowOriginalImages),
            VideoPolicy = VideoCompressionPolicy.From(dto.VideoMaxSide, dto.AllowOriginalVideos)
        };
    }
}
