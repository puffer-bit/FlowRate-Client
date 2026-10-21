namespace Client.Application.Coordinator;

/// <summary>
/// A trusted address answered with a different coordinator than the one it was bound to.
/// </summary>
public record CoordinatorMismatch(
    string Host,
    int Port,
    string ExpectedCoordinatorId,
    string ActualCoordinatorId,
    string? ActualCoordinatorName);
