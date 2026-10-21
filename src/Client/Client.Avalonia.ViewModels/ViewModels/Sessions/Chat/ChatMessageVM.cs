using Client.Services;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using Shared.Enums.User;
using Client.Core.Chat;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

public class ChatMessageVM : ReactiveObject, IMessageListItemVM
{
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromSeconds(1.5);

    public ChatMessageModel Message
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanEdit));
            this.RaisePropertyChanged(nameof(CanDelete));
            this.RaisePropertyChanged(nameof(HasText));
            this.RaisePropertyChanged(nameof(IsMentioningMe));
        }
    }

    /// <summary>
    /// The text as it is shown, with its mentions written as names. Null until the chat renders it,
    /// and for a message without text.
    /// </summary>
    public ChatMessageBody? Body
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// A message that calls the current user, by name or as one of everyone; the user's own included.
    /// </summary>
    public bool IsMentioningMe => Message.Mentions(_currentUserId);

    /// <summary>
    /// Only the author may change a message, and only its text.
    /// </summary>
    public bool CanEdit => Message is { IsMine: true, IsReadOnly: false, IsSystem: false, TextContent: not null };

    /// <summary>The roles of the current user let it delete messages of others.</summary>
    public bool CanManageMessages
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanDelete));
        }
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
    /// The message this one answers, or null when it is not a reply.
    /// </summary>
    public ChatReplyPreviewVM? Reply { get; }
    public bool HasReply => Reply != null;

    public IReadOnlyList<ChatImageVM> Images { get; }
    public bool HasImages => Images.Count > 0;
    public bool HasText => Message.TextContent != null;

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

    /// <summary>
    /// Asked by the view before it opens a link of the message; false leaves the link closed.
    /// </summary>
    public Func<Uri, Task<bool>> ConfirmOpenLink { get; }

    public Interaction<RxVoid, RxVoid> CloseFlyoutInteraction { get; } = new();
    public Interaction<string, RxVoid> CopyMessageInteraction { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> OpenReplyCommand { get; }

    private readonly ReactiveCommand<int, RxVoid> _jumpToMessageCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _replyCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _editCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _deleteCommand;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;

    private readonly int _currentUserId;
    private int _highlightVersion;

    public ChatMessageVM(ChatMessageModel messageModel, int currentUserId,
        ReactiveCommand<ChatMessageModel, RxVoid> replyCommand,
        ReactiveCommand<int, RxVoid> jumpToMessageCommand,
        ReactiveCommand<ChatMessageModel, RxVoid> editCommand,
        ReactiveCommand<ChatMessageModel, RxVoid> deleteCommand,
        ReactiveCommand<int, RxVoid> viewProfileCommand,
        Func<Uri, Task<bool>> confirmOpenLink,
        IReadOnlyList<ChatImageVM>? images = null)
    {
        ConfirmOpenLink = confirmOpenLink;
        _currentUserId = currentUserId;
        Message = messageModel;
        Images = images ?? [];
        Reply = messageModel.ReplyContent != null ? new ChatReplyPreviewVM(messageModel.ReplyContent) : null;
        _replyCommand = replyCommand;
        _editCommand = editCommand;
        _deleteCommand = deleteCommand;
        _jumpToMessageCommand = jumpToMessageCommand;
        _viewProfileCommand = viewProfileCommand;

        // A deleted original has nowhere to jump to
        OpenReplyCommand = ReactiveCommand.Create(OpenReply,
            Reply == null
                ? Observable.Return(false)
                : Reply.WatchProperty(nameof(ChatReplyPreviewVM.IsDeleted), reply => !reply.IsDeleted));
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

    public void ViewProfile() => _ = ViewProfileAsync();

    public async void Highlight()
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

    private async Task ViewProfileAsync()
    {
        await CloseFlyoutInteraction.Handle(RxVoid.Default);

        if (IsSenderDeleted)
            return;

        _viewProfileCommand.Execute(Message.SenderUserId).Subscribe();
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
