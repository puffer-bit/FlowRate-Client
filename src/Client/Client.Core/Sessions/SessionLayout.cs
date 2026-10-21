namespace Client.Core.Sessions;

/// <summary>
/// The order of the session list, the same for every user: the sessions outside any category,
/// then the categories with their sessions.
/// </summary>
/// <param name="Revision">Grows with every change; an older layout than the one shown is dropped.</param>
public sealed record SessionLayout(long Revision, IReadOnlyList<int> RootSessionIds,
    IReadOnlyList<SessionCategory> Categories)
{
    public static readonly SessionLayout Empty = new(0, [], []);

    public SessionCategory? FindCategory(int categoryId) =>
        Categories.FirstOrDefault(category => category.Id == categoryId);

    /// <summary>
    /// The category the session is in; null both outside any category and for a session the layout does not know.
    /// </summary>
    public int? CategoryOf(int sessionId) =>
        Categories.FirstOrDefault(category => category.SessionIds.Contains(sessionId))?.Id;

    /// <summary>
    /// The layout with the session moved the way the server moves it, so the list can show the move
    /// before the server confirms it. A session the layout does not know yet is taken from nowhere.
    /// </summary>
    /// <param name="index">The place among the sessions there once the session is taken out; clamped.</param>
    public SessionLayout WithSessionMoved(int sessionId, int? categoryId, int index)
    {
        if (categoryId is { } id && FindCategory(id) == null)
            return this;

        var root = RootSessionIds.Where(other => other != sessionId).ToList();
        var categories = Categories
            .Select(category => category with { SessionIds = category.SessionIds.Where(other => other != sessionId).ToList() })
            .ToList();

        var target = categoryId is { } targetId
            ? (List<int>)categories.First(category => category.Id == targetId).SessionIds
            : root;
        target.Insert(Math.Clamp(index, 0, target.Count), sessionId);

        return this with { RootSessionIds = root, Categories = categories };
    }

    /// <param name="index">The place among the categories once this one is taken out; clamped.</param>
    public SessionLayout WithCategoryMoved(int categoryId, int index)
    {
        if (FindCategory(categoryId) is not { } moved)
            return this;

        var categories = Categories.Where(category => category.Id != categoryId).ToList();
        categories.Insert(Math.Clamp(index, 0, categories.Count), moved);

        return this with { Categories = categories };
    }
}

public sealed record SessionCategory(int Id, string Name, IReadOnlyList<int> SessionIds);
