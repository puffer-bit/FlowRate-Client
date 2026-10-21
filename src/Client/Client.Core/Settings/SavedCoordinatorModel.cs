namespace Client.Core.Settings;

public class SavedCoordinatorModel
{
    public string CoordinatorId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Address { get; init; } = string.Empty;
    public int Port { get; init; }
    
    public List<SavedUserModel> Users { get; init; } = new();
}