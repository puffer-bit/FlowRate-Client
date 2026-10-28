using Client.Core.Coordinator;
using Client.Avalonia.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using Shared.Enums;
using Shared.Validation;
using Client.Core.Sessions;
using Client.ViewModels.Dialogs;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

/// <summary>
/// The order of the list: the layout from the server, the categories and the moves of the user.
/// </summary>
public partial class SessionsListVM
{
    /// <summary>
    /// The places the menus of the sessions offer: no category first, then every category.
    /// </summary>
    public ObservableCollection<MoveTargetVM> MoveTargets { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> CreateCategoryCommand { get; }

    /// <summary>
    /// Creates a session outside any category, from the menu of the empty part of the list.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CreateSessionCommand { get; }

    private readonly ReactiveCommand<int?, RxVoid> _createSessionCommand;

    // The last layout the server sent; what the list returns to when a move is refused
    private SessionLayout _serverLayout = SessionLayout.Empty;

    // The layout on screen: the server's, or one with a move of the user not confirmed yet
    private SessionLayout _shownLayout = SessionLayout.Empty;

    private readonly Dictionary<int, SessionCategoryVM> _categories = new();
    private readonly HashSet<int> _collapsedCategoryIds = new();

    // A category created here: once it shows up, its name is opened for typing
    private int? _categoryToRename;

    private bool _isRebuildingRows;

    private void ClearList()
    {
        Sessions.Clear();
        Rows.Clear();
        _categories.Clear();
        _collapsedCategoryIds.Clear();
        _serverLayout = SessionLayout.Empty;
        _shownLayout = SessionLayout.Empty;
        while (MoveTargets.Count > 1)
            MoveTargets.RemoveAt(MoveTargets.Count - 1);
    }

    private void SetLayout(SessionLayout layout, IEnumerable<int> collapsedCategoryIds)
    {
        _collapsedCategoryIds.UnionWith(collapsedCategoryIds);
        _serverLayout = layout;
        _shownLayout = layout;
        RebuildRows();
    }

    // Raised on the socket thread
    private void OnSessionLayoutChanged(SessionLayout layout)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (layout.Revision <= _serverLayout.Revision)
                return;

            _serverLayout = layout;
            _shownLayout = layout;
            RebuildRows();
        });
    }

    /// <summary>
    /// Puts the rows into the order of the shown layout. Rows are moved, not made again, so a moved
    /// session keeps its state and does not play its appear effect once more.
    /// </summary>
    private void RebuildRows()
    {
        SyncCategories();

        var sessionsById = new Dictionary<int, ISessionVM>();
        foreach (var session in Sessions)
            sessionsById.TryAdd(session.Model.Id, session);

        var desired = new List<ISessionListRow>(Sessions.Count + _categories.Count);
        var placed = new HashSet<ISessionVM>();

        void Place(int sessionId)
        {
            if (sessionsById.TryGetValue(sessionId, out var session) && placed.Add(session))
                desired.Add(session);
        }

        foreach (var sessionId in _shownLayout.RootSessionIds)
            Place(sessionId);
        var rootEnd = desired.Count;

        foreach (var category in _shownLayout.Categories)
        {
            desired.Add(_categories[category.Id]);
            foreach (var sessionId in category.SessionIds)
                Place(sessionId);
        }

        // A session the layout does not know: one just created, whose place comes with the next
        // layout, or one folding away after it was deleted. It stays behind the row it follows now,
        // so nothing jumps; a new one goes to the end of the sessions outside any category.
        foreach (var session in Sessions)
        {
            if (placed.Contains(session))
                continue;

            var index = rootEnd;
            var current = Rows.IndexOf(session);
            if (current >= 0)
            {
                index = 0;
                for (var i = current - 1; i >= 0; i--)
                {
                    var before = desired.IndexOf(Rows[i]);
                    if (before >= 0)
                    {
                        index = before + 1;
                        break;
                    }
                }
            }

            desired.Insert(index, session);
            if (index <= rootEnd)
                rootEnd++;
        }

        _isRebuildingRows = true;
        try
        {
            for (var i = 0; i < desired.Count; i++)
            {
                var row = desired[i];
                if (i < Rows.Count && ReferenceEquals(Rows[i], row))
                    continue;

                var current = Rows.IndexOf(row);
                if (current > i)
                    Rows.Move(current, i);
                else
                    Rows.Insert(i, row);
            }

            while (Rows.Count > desired.Count)
                Rows.RemoveAt(Rows.Count - 1);
        }
        finally
        {
            _isRebuildingRows = false;
        }

        UpdateFolding();
        RefreshCategoryCounts();
        RestoreSelection();

        if (_categoryToRename is { } renamed && _categories.TryGetValue(renamed, out var created))
        {
            _categoryToRename = null;
            created.BeginRename();
        }
    }

    // Makes a header for every category of the shown layout and drops the ones that are gone
    private void SyncCategories()
    {
        var ids = _shownLayout.Categories.Select(category => category.Id).ToHashSet();

        foreach (var gone in _categories.Keys.Where(id => !ids.Contains(id)).ToList())
            _categories.Remove(gone);

        foreach (var category in _shownLayout.Categories)
        {
            if (_categories.TryGetValue(category.Id, out var existing))
            {
                // The name being typed is the one the user sees, a rename from elsewhere waits for it
                if (!existing.IsEditing)
                    existing.Name = category.Name;
                continue;
            }

            _categories[category.Id] = new SessionCategoryVM(category.Id, category.Name,
                ToggleCategoryCollapse, RenameCategory, DeleteCategory,
                header => _createSessionCommand.Execute(header.Id).Subscribe())
            {
                IsCollapsed = _collapsedCategoryIds.Contains(category.Id),
                CanManage = Access.CanManageSessions,
                CanCreateSessions = Access.CanCreateSessions
            };
        }

        // The menus list the categories in the order of the list
        for (var i = MoveTargets.Count - 1; i >= 1; i--)
        {
            if (MoveTargets[i].CategoryId is not { } id || !ids.Contains(id))
                MoveTargets.RemoveAt(i);
        }
        for (var i = 0; i < _shownLayout.Categories.Count; i++)
        {
            var category = _shownLayout.Categories[i];
            var index = i + 1;
            var current = -1;
            for (var j = 1; j < MoveTargets.Count; j++)
            {
                if (MoveTargets[j].CategoryId == category.Id)
                {
                    current = j;
                    break;
                }
            }

            if (current < 0)
                MoveTargets.Insert(index, new MoveTargetVM(category.Id, category.Name, MoveToCategory));
            else
            {
                MoveTargets[current].Name = category.Name;
                if (current != index)
                    MoveTargets.Move(current, index);
            }
        }
    }

    /// <summary>
    /// Folds the sessions of collapsed categories away, except the one on screen and the hybrid
    /// session the user is in: those stay in sight, as in Discord.
    /// </summary>
    private void UpdateFolding()
    {
        foreach (var session in Sessions)
        {
            session.IsFolded = _shownLayout.CategoryOf(session.Model.Id) is { } categoryId
                               && _categories.TryGetValue(categoryId, out var category)
                               && category.IsCollapsed
                               && !session.IsShown
                               && !session.IsEntered;
        }
    }

    private void RefreshCategoryCounts()
    {
        foreach (var category in _shownLayout.Categories)
        {
            if (!_categories.TryGetValue(category.Id, out var header))
                continue;

            int unread = 0, mentions = 0;
            foreach (var chat in Sessions.OfType<ChatSessionVM>())
            {
                if (!category.SessionIds.Contains(chat.Model.Id))
                    continue;

                // The same rules as the counters of the chats themselves: a mute keeps only the mentions
                if (!chat.IsMuted)
                    unread += chat.UnreadCount;
                mentions += chat.MentionCount;
            }

            header.SetCounts(unread, mentions);
        }
    }

    private void ToggleCategoryCollapse(SessionCategoryVM category)
    {
        category.IsCollapsed = !category.IsCollapsed;
        if (category.IsCollapsed)
            _collapsedCategoryIds.Add(category.Id);
        else
            _collapsedCategoryIds.Remove(category.Id);

        UpdateFolding();

        if (CoordinatorInstanceId is { } coordinatorInstanceId)
            _ = _preferences.SaveCategoryCollapseAsync(coordinatorInstanceId, category.Id, category.IsCollapsed);
    }

    /// <summary>
    /// Moves a session dragged in the list.
    /// </summary>
    /// <param name="categoryId">The category it is dropped into, or null for the sessions outside any category.</param>
    /// <param name="before">The session it is dropped in front of there, or null for the end.</param>
    public Task MoveSession(ISessionVM session, int? categoryId, ISessionVM? before)
    {
        var sessionIds = (categoryId is { } id ? _shownLayout.FindCategory(id)?.SessionIds : _shownLayout.RootSessionIds)
                         ?? [];
        var others = sessionIds.Where(sessionId => sessionId != session.Model.Id).ToList();
        var index = before != null && others.IndexOf(before.Model.Id) is >= 0 and var at ? at : others.Count;

        return MoveSession(session, categoryId, index);
    }

    private Task MoveToCategory(ISessionVM session, int? categoryId) => MoveSession(session, categoryId, int.MaxValue);

    /// <summary>
    /// Moves a session one place up (a negative step) or down within its category, for lists that
    /// are not dragged, as on Android. Nothing happens at the edge.
    /// </summary>
    public Task MoveSessionBy(ISessionVM session, int step)
    {
        var categoryId = _shownLayout.CategoryOf(session.Model.Id);
        var sessionIds = categoryId is { } id ? _shownLayout.FindCategory(id)!.SessionIds : _shownLayout.RootSessionIds;
        var index = sessionIds.ToList().IndexOf(session.Model.Id);
        if (index < 0)
            return Task.CompletedTask;

        var target = Math.Clamp(index + step, 0, sessionIds.Count - 1);
        return target == index ? Task.CompletedTask : MoveSession(session, categoryId, target);
    }

    /// <inheritdoc cref="MoveSessionBy"/>
    public async Task MoveCategoryBy(SessionCategoryVM category, int step)
    {
        // The layout is shared by everyone, so only roles that may arrange it change it
        if (_coordinatorSession is not { } coordinatorSession || !Access.CanManageSessions)
            return;

        var index = _shownLayout.Categories.ToList().FindIndex(other => other.Id == category.Id);
        if (index < 0)
            return;

        var target = Math.Clamp(index + step, 0, _shownLayout.Categories.Count - 1);
        if (target == index)
            return;

        _shownLayout = _shownLayout.WithCategoryMoved(category.Id, target);
        RebuildRows();

        await OnLayoutRequestDone(await coordinatorSession.MoveSessionCategory(category.Id, target));
    }

    private async Task MoveSession(ISessionVM session, int? categoryId, int index)
    {
        if (_coordinatorSession is not { } coordinatorSession || !Access.CanManageSessions)
            return;

        index = Math.Min(index, Limits.SessionLayoutIndexMax);
        var moved = _shownLayout.WithSessionMoved(session.Model.Id, categoryId, index);
        if (SameOrder(moved, _shownLayout))
            return;

        _shownLayout = moved;
        RebuildRows();

        var result = await coordinatorSession.MoveSession(session.Model.Id, categoryId, index);
        await OnLayoutRequestDone(result);
    }

    /// <param name="before">The category it is dropped in front of, or null for the end.</param>
    public async Task MoveCategory(SessionCategoryVM category, SessionCategoryVM? before)
    {
        if (_coordinatorSession is not { } coordinatorSession || !Access.CanManageSessions)
            return;

        var others = _shownLayout.Categories.Where(other => other.Id != category.Id).Select(other => other.Id).ToList();
        var index = before != null && others.IndexOf(before.Id) is >= 0 and var at ? at : others.Count;

        var moved = _shownLayout.WithCategoryMoved(category.Id, index);
        if (SameOrder(moved, _shownLayout))
            return;

        _shownLayout = moved;
        RebuildRows();

        await OnLayoutRequestDone(await coordinatorSession.MoveSessionCategory(category.Id, index));
    }

    private static bool SameOrder(SessionLayout a, SessionLayout b) =>
        a.RootSessionIds.SequenceEqual(b.RootSessionIds)
        && a.Categories.Select(category => category.Id).SequenceEqual(b.Categories.Select(category => category.Id))
        && a.Categories.Zip(b.Categories).All(pair => pair.First.SessionIds.SequenceEqual(pair.Second.SessionIds));

    /// <summary>
    /// A refused change goes back to the last layout of the server; an accepted one is followed by
    /// that layout anyway.
    /// </summary>
    private async Task OnLayoutRequestDone(RequestResult result)
    {
        if (result.IsSuccess)
            return;

        _shownLayout = _serverLayout;
        RebuildRows();

        // A rejection is reported by the main window
        if (!result.IsRejected)
            await ShowLayoutError(result);
    }

    private async Task ShowLayoutError(RequestResult result) =>
        await ShowMessageBox.Handle(new MessageBoxVM(Icon.Fatal, Buttons.Ok, result switch
        {
            { Error: ErrorCode.TooManyCategories } => Strings.Format("sessions.category.tooMany",
                ("limit", Limits.SessionCategoriesMaxCount)),
            { Error: ErrorCode.TimedOut } => Strings.Get("sessions.layout.timedOut"),
            _ => Strings.Format("sessions.layout.failed", ("code", (int?)result.Error))
        }, Strings.Get("common.error")));

    private async Task CreateCategory()
    {
        if (_coordinatorSession is not { } coordinatorSession || !Access.CanManageSessions)
            return;

        var (result, categoryId) = await coordinatorSession.CreateSessionCategory(Strings.Get("sessions.category.newName"));
        if (!result.IsSuccess)
        {
            if (!result.IsRejected)
                await ShowLayoutError(result);
            return;
        }

        // The layout with the new category usually comes before this answer, but need not
        if (categoryId is { } id)
        {
            if (_categories.TryGetValue(id, out var created))
                created.BeginRename();
            else
                _categoryToRename = id;
        }
    }

    private async Task RenameCategory(SessionCategoryVM category, string name)
    {
        if (_coordinatorSession is not { } coordinatorSession || !Access.CanManageSessions)
            return;

        var previous = category.Name;
        category.Name = name;

        var result = await coordinatorSession.RenameSessionCategory(category.Id, name);
        if (result.IsSuccess)
            return;

        category.Name = _serverLayout.FindCategory(category.Id)?.Name ?? previous;
        if (!result.IsRejected)
            await ShowLayoutError(result);
    }

    private async Task DeleteCategory(SessionCategoryVM category)
    {
        if (_coordinatorSession is not { } coordinatorSession || !Access.CanManageSessions)
            return;

        if (await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
                Strings.Format("sessions.category.delete.question", ("category", category.Name)),
                Strings.Get("sessions.category.delete.title"))) != Result.Yes)
            return;

        var result = await coordinatorSession.DeleteSessionCategory(category.Id);
        if (result.IsSuccess)
        {
            _ = _preferences.SaveCategoryCollapseAsync(coordinatorSession.GetCurrentUser().CoordinatorInstanceId, category.Id, false);
            return;
        }

        if (!result.IsRejected)
            await ShowLayoutError(result);
    }

    /// <summary>
    /// The category the row belongs to: its own for a header, the one a session is in, or null.
    /// </summary>
    public int? CategoryOf(ISessionListRow row) => row switch
    {
        SessionCategoryVM category => category.Id,
        ISessionVM session => _shownLayout.CategoryOf(session.Model.Id),
        _ => null
    };
}
