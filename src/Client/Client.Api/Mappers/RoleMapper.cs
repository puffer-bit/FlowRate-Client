using Shared.Models.DataTransferObjects;
using Client.Core.Coordinator;

namespace Client.Api.Mappers;

internal static class RoleMapper
{
    public static RoleModel ToModel(this RoleDTO dto) =>
        new(dto.Id, dto.Name, dto.Color, dto.Position, dto.Permissions, dto.IsEveryone, dto.Revision);

    public static IReadOnlyList<RoleModel> ToModels(this IEnumerable<RoleDTO> dtos) =>
        dtos.Select(ToModel).ToList();

    public static RoleUpdateDTO ToDto(this RoleChange change) => new()
    {
        Name = change.Name,
        Color = change.Color,
        Permissions = change.Permissions
    };
}
