namespace Client.Application.Ports.Repositories;

/// <summary>
/// The sections of the settings dialog that the user has collapsed, by their keys.
/// </summary>
public interface ICollapsedSectionsRepository
{
    Task<IReadOnlyList<string>> GetAsync();
    Task SetAsync(string sectionKey, bool isCollapsed);
}
