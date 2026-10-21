using Shared.Enums.User;

namespace Client.Core.Users;

public class UserModel
{
    public required int Id { get; init; }
    public required string CoordinatorInstanceId { get; init; }
    public required string Username { get; init; }
    public string? Ip { get; init; }
    public string? RegionName { get; init; }
    public string? RegionCode { get; init; }
    public string? DisplayName { get; init; }
    public string? Prefix { get; init; }
    public string? AvatarUrl { get; init; }
    public UserState State { get; init; }
    public UserStatus Status { get; init; }
    public DateTime FirstConnectionTime { get; init; }
    public DateTime LastConnectionTime { get; init; }

    /// <summary>The coordinator roles of the user, without the everyone role.</summary>
    public IReadOnlyList<int> RoleIds { get; init; } = [];
}