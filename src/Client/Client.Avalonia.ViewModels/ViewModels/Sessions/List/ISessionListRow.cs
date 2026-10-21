namespace Client.ViewModels.Sessions.List;

/// <summary>
/// One row of the session list: a session or the header of a category.
/// </summary>
public interface ISessionListRow
{
    /// <summary>
    /// The row is hidden inside a collapsed category. It stays in the list, so expanding the
    /// category shows it again with its appear effect and in its place.
    /// </summary>
    bool IsFolded { get; }

    /// <summary>
    /// The row is being dragged to another place; the list dims it meanwhile.
    /// </summary>
    bool IsDragged { get; set; }
}
