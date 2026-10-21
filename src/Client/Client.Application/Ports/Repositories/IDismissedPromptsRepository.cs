namespace Client.Application.Ports.Repositories;

/// <summary>
/// The questions the user asked not to be shown again, by their keys.
/// </summary>
public interface IDismissedPromptsRepository
{
    Task<bool> IsDismissedAsync(string promptKey);
    Task SetAsync(string promptKey, bool isDismissed);
}
