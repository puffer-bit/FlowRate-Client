using System;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Core.Chat;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

/// <summary>
/// The header of a category of the session list. Its sessions follow it as rows of their own.
/// </summary>
public sealed class SessionCategoryVM : ReactiveObject, ISessionListRow
{
    public int Id { get; }

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The user folded the category away. Kept per user and per device, not on the server.
    /// </summary>
    public bool IsCollapsed
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseBadgeProperties();
        }
    }

    public bool IsFolded => false;

    /// <summary>The roles of the current user let it rename and delete categories.</summary>
    public bool CanManage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The roles of the current user let it create sessions.</summary>
    public bool CanCreateSessions
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDragged
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// A session dragged over the header would be put into the category.
    /// </summary>
    public bool IsDropTarget
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The unread messages of the chats inside, counted the way each chat counts its own.
    /// </summary>
    public int UnreadCount
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseBadgeProperties();
        }
    }

    public int MentionCount
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseBadgeProperties();
        }
    }

    /// <summary>
    /// The name is drawn brighter while a chat inside is unread and the category is collapsed.
    /// </summary>
    public bool HasUnread => IsCollapsed && (UnreadCount > 0 || MentionCount > 0);

    // An expanded category shows the counters of its chats, so it shows none of its own
    public bool ShowsUnreadCount => IsCollapsed && UnreadCount > 0 && MentionCount == 0;
    public bool ShowsMentionCount => IsCollapsed && MentionCount > 0;
    public string UnreadCountText => CountText(UnreadCount);
    public string MentionCountText => "@" + CountText(MentionCount);

    /// <summary>
    /// The name is being typed in place of the header.
    /// </summary>
    public bool IsEditing
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string EditedName
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsEditedNameValid));
        }
    } = "";

    public bool IsEditedNameValid => CoordinatorSession.ValidateCategoryName(EditedName);

    public ReactiveCommand<RxVoid, RxVoid> ToggleCollapseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> BeginRenameCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CommitRenameCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelRenameCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CreateSessionCommand { get; }

    public SessionCategoryVM(int id, string name,
        Action<SessionCategoryVM> toggleCollapse,
        Func<SessionCategoryVM, string, Task> rename,
        Func<SessionCategoryVM, Task> delete,
        Action<SessionCategoryVM> createSession)
    {
        Id = id;
        Name = name;

        ToggleCollapseCommand = ReactiveCommand.Create(() => toggleCollapse(this));
        BeginRenameCommand = ReactiveCommand.Create(BeginRename);
        CommitRenameCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            var name = EditedName.Trim();
            if (name != Name && CoordinatorSession.ValidateCategoryName(name))
                await rename(this, name);
        });
        CancelRenameCommand = ReactiveCommand.Create(() => { IsEditing = false; });
        DeleteCommand = ReactiveCommand.CreateFromTask(() => delete(this));
        CreateSessionCommand = ReactiveCommand.Create(() => createSession(this));
    }

    public void BeginRename()
    {
        EditedName = Name;
        IsEditing = true;
    }

    public void SetCounts(int unreadCount, int mentionCount)
    {
        UnreadCount = unreadCount;
        MentionCount = mentionCount;
    }

    private void RaiseBadgeProperties()
    {
        this.RaisePropertyChanged(nameof(HasUnread));
        this.RaisePropertyChanged(nameof(ShowsUnreadCount));
        this.RaisePropertyChanged(nameof(ShowsMentionCount));
        this.RaisePropertyChanged(nameof(UnreadCountText));
        this.RaisePropertyChanged(nameof(MentionCountText));
    }

    // The same cap as the counter of a chat
    private static string CountText(int count) =>
        count >= ChatReadState.MaxCount ? $"{ChatReadState.MaxCount - 1}+" : count.ToString();
}
