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

    /// <summary>The device the user connected from last; null while they look offline.</summary>
    public UserDeviceModel? Device { get; init; }

    /// <summary>Online from a phone: shown with a phone instead of the status dot.</summary>
    public bool IsMobile => Device?.IsMobile == true;
    public DateTimeOffset FirstConnectionTime { get; init; }
    public DateTimeOffset LastConnectionTime { get; init; }

    /// <summary>The coordinator roles of the user, without the everyone role.</summary>
    public IReadOnlyList<int> RoleIds { get; init; } = [];
}