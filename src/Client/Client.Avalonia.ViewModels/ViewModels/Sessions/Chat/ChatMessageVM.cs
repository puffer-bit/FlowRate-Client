using Shared.Models.DataTransferObjects.ChatSession.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using Client.Avalonia.Localization;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Reactions;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;
using Shared.Enums.User;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

public class ChatMessageVM : ReactiveObject, IMessageListItemVM
{
    public DateTime LocalSentTime => Message.SentTime.Kind != DateTimeKind.Local
        ? Message.SentTime.ToLocalTime()
        : Message.SentTime;

    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(1.5);

    public ChatMessageModel Message
    {
        get;
        set
        {
            var result = value.TextContent?.CommandResult;
            if (result != CommandResult?.Result)
            {
                CommandResult?.Finish();
                CommandResult = result != null && result.IsValid() ? new ChatCommandResultVM(result) : null;
                this.RaisePropertyChanged(nameof(CommandResult));
                this.RaisePropertyChanged(nameof(HasCommandResult));
                this.RaisePropertyChanged(nameof(HasPlainText));
            }
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsSystem));
            this.RaisePropertyChanged(nameof(SystemEventKind));
            SystemParts = ChatSystemMessageText.Parts(value, _ => null);
            this.RaisePropertyChanged(nameof(LocalSentTime));
            this.RaisePropertyChanged(nameof(IsPinned));
            this.RaisePropertyChanged(nameof(CanEdit));
            this.RaisePropertyChanged(nameof(CanReact));
            this.RaisePropertyChanged(nameof(Reactions));
            this.RaisePropertyChanged(nameof(ReactionChoices));
            this.RaisePropertyChanged(nameof(HasReactions));
            this.RaisePropertyChanged(nameof(CanDelete));
            this.RaisePropertyChanged(nameof(CanPin));
            this.RaisePropertyChanged(nameof(HasText));
            this.RaisePropertyChanged(nameof(HasPlainText));
            this.RaisePropertyChanged(nameof(IsMentioningMe));
        }
    }

    public bool IsSystem => Message.IsSystem;
    public ChatSystemEventKind SystemEventKind => Message.IsSystem && Message.SystemContent?.IsValid() == true
        ? Message.SystemContent.Kind : 0;
    public IReadOnlyList<ChatSystemMessagePart> SystemParts
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(SystemText));
        }
    } = [];
    public string SystemText => string.Concat(SystemParts.Select(part => part.Text));
    public ReactiveCommand<int, RxVoid> OpenSystemUserCommand => _viewProfileCommand;

    /// <summary>
    /// The text as it is shown, with its mentions written as names. Null until the chat renders it,
    /// and for a message without text.
    /// </summary>
    public ChatMessageBody? Body
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsJumboEmoji));
        }
    }

    /// <summary>
    /// The text is a few emoji alone and is shown large.
    /// </summary>
    public bool IsJumboEmoji => ChatEmoji.IsJumbo(Body?.Text);

    /// <summary>
    /// A message that calls the current user, by name or as one of everyone; the user's own included.
    /// </summary>
    public bool IsMentioningMe => Message.Mentions(_currentUserId);

    /// <summary>
    /// Only the author may change a message, and only its text.
    /// </summary>
    public bool CanEdit => Message.TextContent?.CommandResult == null && Message is { IsMine: true, IsReadOnly: false, IsSystem: false, TextContent: not null };

    /// <summary>The roles of the current user let it delete messages of others.</summary>
    public bool CanManageMessages
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanDelete));
            this.RaisePropertyChanged(nameof(CanPin));
        }
    }

    public bool IsPinned => Message.IsPinned;
    public bool IsPinAllowed { get; set; }
    public string? PinError
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasPinError));
        }
    }
    public bool HasPinError => PinError != null;
    private bool _settingPin;
    public bool CanPinInDirectChat { get; init; }
    public bool CanPin => SetPinned != null && (CanManageMessages || CanPinInDirectChat) && IsPinAllowed && !Message.IsSystem && !Message.IsDeleted
                          && _composer is { IsConnected: true };
    public Func<bool, Task<bool>>? SetPinned { get; init; }
    public void TogglePinned() => _ = TogglePinnedAsync();
    private async Task TogglePinnedAsync()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);
        await SetPinStateAsync(!IsPinned);
    }

    public Task UnpinAsync() => SetPinStateAsync(false);

    private async Task SetPinStateAsync(bool pinned)
    {
        if (!CanPin || SetPinned == null || _settingPin || IsPinned == pinned) return;
        _settingPin = true;
        PinError = null;
        try
        {
            if (!await SetPinned(pinned)) PinError = Strings.Get("chat.pinned.failed");
        }
        catch { PinError = Strings.Get("chat.pinned.failed"); }
        finally { _settingPin = false; }
    }

    /// <summary>The author deletes their messages; roles that manage messages delete any.</summary>
    public bool CanDelete => Message is { IsSystem: false } && (Message.IsMine || CanManageMessages);

    /// <summary>
    /// Null until the author is resolved by the chat.
    /// </summary>
    public string? SenderName
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The author was deleted: the name is a placeholder and there is no profile to show.
    /// </summary>
    public bool IsSenderDeleted
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The avatar of the author, known together with the name.
    /// </summary>
    public string? SenderAvatarUrl
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The status of the author, known together with the name.
    /// </summary>
    public UserStatus? SenderStatus
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The color of the highest colored role of the author, 0xRRGGBB; null keeps the usual one.
    /// </summary>
    public int? SenderColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The message this one answers, or null when it is not a reply.
    /// </summary>
    public ChatReplyPreviewVM? Reply { get; }
    public bool HasReply => Reply != null;

    public IReadOnlyList<ChatImageVM> Images { get; }
    public bool HasImages => Images.Count > 0;

    /// <summary>The files of the message that are downloaded rather than shown.</summary>
    public IReadOnlyList<ChatFileVM> Files { get; }
    public bool HasFiles => Files.Count > 0;
    public bool HasText => Message.TextContent != null;
    public ChatCommandResultVM? CommandResult { get; private set; }
    public bool HasCommandResult => CommandResult != null;
    public bool HasPlainText => HasText && !HasCommandResult;

    public bool TakeLiveArrival()
    {
        if (!IsNew) return false;
        IsNew = false;
        CommandResult?.Play();
        return true;
    }

    public bool IsSequenceStart
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSequenceEnd
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsFlyoutOpen
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Briefly set after the chat jumped to this message, so the user sees where they landed.
    /// </summary>
    public bool IsHighlighted
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Arrived while the chat was open, as opposed to loaded with the history. The view plays the
    /// appear animation for such a message once and clears the flag, so scrolling it out of view
    /// and back does not play it again.
    /// </summary>
    public bool IsNew { get; set; }

    /// <summary>
    /// What the copy item of the flyout copies: the selection, or the whole text without one.
    /// Set by the view when the flyout opens.
    /// </summary>
    public string? CopyText
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsTextSelected
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasCopyText => CopyText != null;

    /// <summary>
    /// The link the flyout was opened over, as it is written in the message; null when it was
    /// opened elsewhere. Set by the view before the flyout opens.
    /// </summary>
    public string? CopyLinkText
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasCopyLink));
        }
    }

    public bool HasCopyLink => CopyLinkText != null;

    /// <summary>The image under the pointer when the message menu opens.</summary>
    public ChatImageVM? CopyImageTarget
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasCopyImage));
        }
    }

    public bool HasCopyImage => CopyImageTarget != null;

    /// <summary>The file under the pointer when the message menu opens.</summary>
    public ChatFileVM? ContextFile
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasContextFile));
        }
    }

    public bool HasContextFile => ContextFile != null;

    /// <summary>
    /// Asked by the view before it opens a link of the message; false leaves the link closed.
    /// </summary>
    public Func<Uri, Task<bool>> ConfirmOpenLink { get; }

    public Interaction<RxVoid, RxVoid> CloseFlyoutInteraction { get; } = new();
    public Interaction<string, RxVoid> CopyMessageInteraction { get; } = new();
    public Interaction<ChatImageVM, RxVoid> CopyImageInteraction { get; } = new();
    public Interaction<ChatImageVM, RxVoid> SaveImageInteraction { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> OpenReplyCommand { get; }

    public bool IsPinnedListItem { get; internal set; }

    public void GoToMessage() => _ = GoToMessageAsync();

    private async Task GoToMessageAsync()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);
        _jumpToMessageCommand.Execute(Message.Id).Subscribe();
    }

    private readonly ReactiveCommand<int, RxVoid> _jumpToMessageCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _replyCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _editCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _deleteCommand;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;
    private readonly ReactiveCommand<int, RxVoid>? _openDirectChatCommand;
    private readonly ChatComposerVM? _composer;

    public Func<string, bool, Task<bool>>? SetReaction { get; init; }

    public bool IsReactionAllowed { get; set; } = true;

    public bool CanReact => IsReactionAllowed && SetReaction != null && Message is { IsSystem: false, IsReadOnly: false, IsDeleted: false }
                            && _composer is { CanWrite: true, IsConnected: true };

    public IReadOnlyList<ChatReactionVM> Reactions => Message.Reactions
        .GroupBy(reaction => reaction.Emoji)
        .Select(group => new ChatReactionVM(group.Key, group.Select(r => r.UserId).Distinct().Count(),
            group.Any(r => r.UserId == _currentUserId), ReactAsync)).ToList();

    public IReadOnlyList<ChatReactionVM> ReactionChoices => ChatReactionEmoji.All.Select(emoji =>
        new ChatReactionVM(emoji, Message.Reactions.Count(r => r.Emoji == emoji),
            Message.Reactions.Any(r => r.Emoji == emoji && r.UserId == _currentUserId), ReactAsync)).ToList();

    public bool HasReactions => Message.Reactions.Count > 0;

    public string? ReactionError
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasReactionError));
        }
    }
    public bool HasReactionError => ReactionError != null;
    private bool _isSettingReaction;

    private async Task ReactAsync(string emoji, bool closeMenu)
    {
        if (closeMenu)
            await CloseFlyoutInteraction.Handle(RxVoid.Default);
        if (!CanReact || _isSettingReaction)
            return;

        _isSettingReaction = true;
        ReactionError = null;
        try
        {
            var add = !Message.Reactions.Any(r => r.UserId == _currentUserId && r.Emoji == emoji);
            if (!await SetReaction!(emoji, add))
                ReactionError = Strings.Get("chat.reaction.failed");
        }
        finally
        {
            _isSettingReaction = false;
        }
    }

    public bool CanMentionUser => _composer is { CanWrite: true, IsConnected: true };

    public void MentionUser(int userId, string name) => _composer?.MentionUser(userId, name);

    public bool CanOpenDirectChat(int userId) => userId != _currentUserId && _openDirectChatCommand != null;
    public void OpenDirectChat(int userId) => _openDirectChatCommand?.Execute(userId).Subscribe();
    public void OpenUserProfile(int userId) => _viewProfileCommand.Execute(userId).Subscribe();

    private readonly int _currentUserId;
    private int _highlightVersion;

    public ChatMessageVM(ChatMessageModel messageModel, int currentUserId,
        ReactiveCommand<ChatMessageModel, RxVoid> replyCommand,
        ReactiveCommand<int, RxVoid> jumpToMessageCommand,
        ReactiveCommand<ChatMessageModel, RxVoid> editCommand,
        ReactiveCommand<ChatMessageModel, RxVoid> deleteCommand,
        ReactiveCommand<int, RxVoid> viewProfileCommand,
        Func<Uri, Task<bool>> confirmOpenLink,
        IReadOnlyList<ChatImageVM>? images = null,
        IReadOnlyList<ChatFileVM>? files = null,
        ChatComposerVM? composer = null,
        ReactiveCommand<int, RxVoid>? openDirectChatCommand = null)
    {
        ConfirmOpenLink = confirmOpenLink;
        _currentUserId = currentUserId;
        Message = messageModel;
        Images = images ?? [];
        Files = files ?? [];
        Reply = messageModel.ReplyContent != null ? new ChatReplyPreviewVM(messageModel.ReplyContent) : null;
        _replyCommand = replyCommand;
        _editCommand = editCommand;
        _deleteCommand = deleteCommand;
        _jumpToMessageCommand = jumpToMessageCommand;
        _viewProfileCommand = viewProfileCommand;
        _openDirectChatCommand = openDirectChatCommand;
        _composer = composer;

        // A deleted original has nowhere to jump to
        OpenReplyCommand = ReactiveCommand.Create(OpenReply,
            Reply == null
                ? Observable.Return(false)
                : Reply.WatchProperty(nameof(ChatReplyPreviewVM.IsDeleted), reply => !reply.IsDeleted));
    }

    public void RefreshReactionAccess()
    {
        this.RaisePropertyChanged(nameof(CanReact));
        this.RaisePropertyChanged(nameof(CanPin));
    }

    public void SetCopyText(string? text, bool isTextSelected)
    {
        CopyText = text;
        IsTextSelected = isTextSelected;
        this.RaisePropertyChanged(nameof(HasCopyText));
    }

    // The flyout items are bound to these methods, so a message does not need commands of its own
    public void Delete() => _ = DeleteMessage();

    public void StartReply() => _ = StartReplyAsync();

    public void StartEdit() => _ = StartEditAsync();

    public void Copy() => _ = CopyMessage();

    public void CopyLink() => _ = CopyLinkAsync();

    public void CopyImage() => _ = CopyImageAsync();

    public void SaveImageAs() => _ = SaveImageAsAsync();

    public void SaveFileAs() => _ = SaveFileAsAsync();

    public void ViewProfile() => _ = ViewProfileAsync();

    public void OpenSenderProfile()
    {
        if (!IsSenderDeleted)
            OpenUserProfile(Message.SenderUserId);
    }

    public void Highlight() => _ = HighlightAsync();

    private async Task HighlightAsync()
    {
        int version = ++_highlightVersion;
        IsHighlighted = true;

        await Task.Delay(HighlightDuration);

        // A newer jump to the same message restarts the highlight instead of cutting it short
        if (version == _highlightVersion)
            IsHighlighted = false;
    }

    private async Task DeleteMessage()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        if (!CanDelete)
            return;

        _deleteCommand.Execute(Message).Subscribe();
    }

    private async Task CopyMessage()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        if (CopyText != null)
            await CopyMessageInteraction.Handle(CopyText);
    }

    private async Task CopyLinkAsync()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        if (CopyLinkText != null)
            await CopyMessageInteraction.Handle(CopyLinkText);
    }

    private async Task CopyImageAsync()
    {
        var image = CopyImageTarget;
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        if (image != null)
            await CopyImageInteraction.Handle(image);
    }

    private async Task SaveImageAsAsync()
    {
        var image = CopyImageTarget;
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        if (image != null)
            await SaveImageInteraction.Handle(image);
    }

    private async Task SaveFileAsAsync()
    {
        var file = ContextFile;
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        if (file is { IsDownloading: false })
            file.DownloadCommand.Execute().Subscribe();
    }

    private async Task ViewProfileAsync()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        OpenSenderProfile();
    }

    private async Task StartReplyAsync()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        _replyCommand.Execute(Message).Subscribe();
    }

    private async Task StartEditAsync()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        _editCommand.Execute(Message).Subscribe();
    }

    private void OpenReply()
    {
        _jumpToMessageCommand.Execute(Reply!.MessageId).Subscribe();
    }
}
