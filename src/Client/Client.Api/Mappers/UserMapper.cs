using Shared.Models.DataTransferObjects;
using Client.Core.Users;

namespace Client.Api.Mappers;

internal static class UserMapper
{
    public static UserModel ToModel(this UserDTO dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        return new UserModel
        {
            Id = dto.Id,
            CoordinatorInstanceId = dto.CoordinatorInstanceId,
            Username = dto.Username,
            Ip = dto.Ip,
            RegionName = dto.RegionName,
            RegionCode = dto.RegionCode,
            DisplayName = dto.DisplayName,
            Prefix = dto.Prefix,
            AvatarUrl = dto.AvatarUrl,
            State = dto.State,
            Status = dto.Status,
            FirstConnectionTime = dto.FirstConnectionTime,
            LastConnectionTime = dto.LastConnectionTime,
            RoleIds = dto.RoleIds
        };
    }
}
