namespace Client.Application.Ports.Repositories;

/// <summary>
/// The widths the user dragged the resizable panels to, by the keys of the panels.
/// </summary>
public interface IPanelWidthsRepository
{
    /// <returns>The width, or null while the panel has never been resized.</returns>
    Task<double?> GetAsync(string panelKey);
    Task SetAsync(string panelKey, double width);
}
