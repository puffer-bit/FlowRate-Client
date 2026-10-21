using Shared.Enums;

namespace Client.Core.Coordinator;

/// <summary>
/// A role of the coordinator. Higher <see cref="Position"/> ranks higher; the everyone role is at 0 and every
/// user has it without it being given.
/// </summary>
/// <param name="Color">0xRRGGBB, or null for no color.</param>
/// <param name="Revision">Grows with every saved change; a change is accepted only against the current one.</param>
public sealed record RoleModel(
    int Id,
    string Name,
    int? Color,
    int Position,
    Permission Permissions,
    bool IsEveryone,
    long Revision);

/// <summary>The editable values of a role. The everyone role keeps its name and has no color.</summary>
public sealed record RoleChange(string Name, int? Color, Permission Permissions);
