using Shared.Enums;

namespace Client.Core.Sessions;

public class SessionModel
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int? Capacity { get; init; }
    public required SessionType SessionType { get; init; }
    public bool IsEntered { get; init; }
}