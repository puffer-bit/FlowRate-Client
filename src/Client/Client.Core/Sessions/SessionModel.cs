using Shared.Enums;
using Shared.Permissions;

namespace Client.Core.Sessions;

public class SessionModel
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int? Capacity { get; init; }
    public required SessionType SessionType { get; init; }
    public bool IsEntered { get; init; }

    /// <summary>What roles allow and deny in this session, apart from what they allow on the coordinator.</summary>
    public IReadOnlyList<PermissionOverride> PermissionOverrides { get; init; } = [];
}