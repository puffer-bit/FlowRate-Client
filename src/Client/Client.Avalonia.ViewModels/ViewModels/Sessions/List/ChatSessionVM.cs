using System;
using System.Linq;
using Client.Avalonia.Localization;
using System.Collections.Generic;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.ViewModels.Dialogs;
using ReactiveUI;
using Shared.Enums;

using Result = Client.ViewModels.Dialogs.Result;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

public class ChatSessionVM : ReactiveObject, ISessionVM, IDisposable
{
    // Session data
    public SessionModel Model
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            DisplayName = value.Name;
            this.RaisePropertyChanged(nameof(IsPrivate));
            this.RaisePropertyChanged(nameof(IsDirect));
            this.RaisePropertyChanged(nameof(IsGroup));
            this.RaisePropertyChanged(nameof(LastMessagePreview));
            this.RaisePropertyChanged(nameof(LastMessageTime));
        }
    }

    public string? AvatarUrl { get; set { this.RaiseAndSetIfChanged(ref field, value); } }
    public string DisplayName { get; set { this.RaiseAndSetIfChanged(ref field, value); } } = string.Empty;
    public ReactiveCommand<RxVoid, RxVoid>? EditGroupCommand { get; set; }
    public ReactiveCommand<RxVoid, RxVoid>? LeaveGroupCommand { get; set; }
    public bool CanEditGroup => Model is ChatSessionModel { Kind: ChatKind.Group } && CanManage;
    public bool CanLeaveGroup => Model is ChatSessionModel { Kind: ChatKind.Group } && !CanManage;
    public bool IsDirect => Model is ChatSessionModel { Kind: ChatKind.Direct };
    public bool IsGroup => Model is ChatSessionModel { Kind: ChatKind.Group };
    public bool IsPrivate => Model is ChatSessionModel { IsPrivate: true };
    public string LastMessageTime => (Model as ChatSessionModel)?.LastMessage?.SentTime.ToLocalTime().ToString("HH:mm") ?? "";
    public string LastMessagePreview
    {
        get
        {
            if ((Model as ChatSessionModel)?.LastMessage is not { } message) return Strings.Get("privateChats.empty");
            if (message.IsSystem && message.SystemContent?.Kind == Shared.Models.DataTransferObjects.ChatSession.Messages.ChatSystemEventKind.CallEnded)
                return Client.ViewModels.Sessions.Chat.ChatSystemMessageText.Format(message, _ => null);
            if (!string.IsNullOrWhiteSpace(message.TextContent?.Text))
                return message.TextContent.Text.Replace('\n', ' ').Replace('\r', ' ');
            if (message.Images.Count > 0) return Strings.Get("privateChats.photo");
            if (message.Video.Count > 0) return Strings.Get("privateChats.video");
            if (message.Files.Any(file => file.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
                return Strings.Get("privateChats.audio");
            if (message.Files.Count > 0) return Strings.Get("privateChats.file");
            return Strings.Get("privateChats.message");
        }
    }

    // UI
    public bool IsEntered
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsShown
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseUnreadProperties();
        }
    }

    public bool IsLeaving
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsFolded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDragged
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IReadOnlyList<MoveTargetVM> MoveTargets { get; set; } = [];

    /// <summary>The roles of the current user let it delete the chat.</summary>
    public bool CanManage
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The roles of the current user let it move the chat to another category.</summary>
    public bool CanArrange => !IsPrivate && CanManage;

    // Chats have no host
    public bool IsHost => false;

    /// <summary>The roles of the current user let it see the chat. The server sends no chat it may not see.</summary>
    public bool CanView
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>The roles of the current user let it change what roles may do in the chat.</summary>
    public bool CanEditPermissions
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public void ApplyAccess(CoordinatorPermissions permissions)
    {
        CanManage = IsPrivate ? permissions.HasIn(Model, Permission.ManageSessions) : permissions.Has(Permission.ManageSessions);
        CanView = permissions.HasIn(Model, Permission.ViewSessions);
        CanEditPermissions = !IsPrivate && permissions.Has(Permission.ManageSessions | Permission.ManageRoles);
        this.RaisePropertyChanged(nameof(CanArrange));
        this.RaisePropertyChanged(nameof(CanEditGroup));
        this.RaisePropertyChanged(nameof(CanLeaveGroup));
    }

    /// <summary>
    /// The user muted the notifications of the chat. The list marks it.
    /// </summary>
    public bool IsMuted
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseUnreadProperties();
        }
    }

    /// <summary>
    /// Messages of others after the read position of the user, up to <see cref="ChatReadState.MaxCount"/>.
    /// </summary>
    public int UnreadCount
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseUnreadProperties();
        }
    }

    /// <summary>
    /// The unread messages that mention the user by id or with @everyone.
    /// </summary>
    public int MentionCount
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            RaiseUnreadProperties();
        }
    }

    /// <summary>
    /// The list draws the name brighter. A muted chat is marked only by mentions, as in Discord.
    /// </summary>
    public bool HasUnread => MentionCount > 0 || (UnreadCount > 0 && !IsMuted);

    /// <summary>
    /// The plain counter; a chat with mentions shows the mention counter instead. The shown chat
    /// has no counters: they go away on the click, not once its first page has loaded and read them.
    /// </summary>
    public bool ShowsUnreadCount => UnreadCount > 0 && MentionCount == 0 && !IsMuted && !IsShown;

    /// <summary>
    /// Shown in muted chats too: a mention gets through the mute.
    /// </summary>
    public bool ShowsMentionCount => MentionCount > 0 && !IsShown;

    /// <summary>
    /// The crossed bell of a muted chat, in the place of the counters; a mention counter takes it.
    /// </summary>
    public bool ShowsMutedMark => IsMuted && !ShowsMentionCount;

    public string UnreadCountText => CountText(UnreadCount);
    public string MentionCountText => "@" + CountText(MentionCount);

    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; }

    // Reactive commands
    public ReactiveCommand<int, RxVoid> DeleteSessionCommand { get; set; }
    public ReactiveCommand<SessionModel, RxVoid> ShowSessionCommand { get; set; }
    public ReactiveCommand<int, RxVoid> ToggleMuteCommand { get; set; }
    public ReactiveCommand<SessionModel, RxVoid> EditPermissionsCommand { get; }
    public ReactiveCommand<int, RxVoid> LeaveChatCommand { get; }

    public ChatSessionVM(SessionModel model,
        ReactiveCommand<SessionModel, RxVoid> showSessionCommand,
        ReactiveCommand<int, RxVoid> deleteSessionCommand,
        ReactiveCommand<int, RxVoid> toggleMuteCommand,
        ReactiveCommand<SessionModel, RxVoid> editPermissionsCommand,
        ReactiveCommand<int, RxVoid> leaveChatCommand)
    {
        Model = model;
        Strings.LanguageChanged += OnLanguageChanged;

        ShowMessageBox = new Interaction<MessageBoxVM, Result>();

        ShowSessionCommand = showSessionCommand;
        DeleteSessionCommand = deleteSessionCommand;
        ToggleMuteCommand = toggleMuteCommand;
        EditPermissionsCommand = editPermissionsCommand;
        LeaveChatCommand = leaveChatCommand;
    }

    private void OnLanguageChanged() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        this.RaisePropertyChanged(nameof(LastMessagePreview)));

    public void Dispose()
    {
        Strings.LanguageChanged -= OnLanguageChanged;
        EditGroupCommand?.Dispose();
        LeaveGroupCommand?.Dispose();
    }

    public void ApplyReadState(ChatReadState state)
    {
        UnreadCount = state.UnreadCount;
        MentionCount = state.MentionCount;
    }

    private void RaiseUnreadProperties()
    {
        this.RaisePropertyChanged(nameof(HasUnread));
        this.RaisePropertyChanged(nameof(ShowsUnreadCount));
        this.RaisePropertyChanged(nameof(ShowsMentionCount));
        this.RaisePropertyChanged(nameof(ShowsMutedMark));
        this.RaisePropertyChanged(nameof(UnreadCountText));
        this.RaisePropertyChanged(nameof(MentionCountText));
    }

    // The server stops counting at the limit, so reaching it means "more than shown"
    private static string CountText(int count) =>
        count >= ChatReadState.MaxCount ? $"{ChatReadState.MaxCount - 1}+" : count.ToString();
}
