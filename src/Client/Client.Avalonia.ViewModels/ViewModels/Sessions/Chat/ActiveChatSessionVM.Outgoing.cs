using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Client.Core.Chat;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The messages of this chat on their way to the server (see <see cref="ChatOutbox"/>): shown at once at the end of
/// the chat, and replaced in their row by the stored message once it comes back with the same client id. They are in
/// the list only while it reaches the newest messages.
/// </summary>
public partial class ActiveChatSessionVM
{
    private readonly ChatOutbox? _outbox;
    private readonly string _coordinatorInstanceId;

    // The messages on their way by their client id, and those of them the list shows now
    private readonly Dictionary<Guid, ChatMessageVM> _outgoingVms = new();
    private readonly HashSet<ChatMessageVM> _shownOutgoing = new();

    private void AttachOutbox()
    {
        if (_outbox is not { } outbox)
            return;

        _subscriptions
            .Add(() => outbox.Added += OnOutgoingAdded, () => outbox.Added -= OnOutgoingAdded)
            .Add(() => outbox.Removed += OnOutgoingRemoved, () => outbox.Removed -= OnOutgoingRemoved)
            .Add(() => outbox.Sent += OnOutgoingSent, () => outbox.Sent -= OnOutgoingSent);

        foreach (var message in outbox.Get(_coordinatorInstanceId, Id))
            _outgoingVms[message.ClientMessageId] = CreateOutgoingView(message, isNew: false);
        SyncOutgoingRows();
    }

    private bool IsOfThisChat(ChatOutgoingMessage message) =>
        message.ChatId == Id && message.CoordinatorInstanceId == _coordinatorInstanceId;

    private void OnOutgoingAdded(ChatOutgoingMessage message)
    {
        if (_isDisposed || !IsOfThisChat(message) || _outgoingVms.ContainsKey(message.ClientMessageId))
            return;

        // Read back after a restart, it may have arrived before the client went down
        if (_messageVms.Values.Any(loaded => loaded.Message.ClientMessageId == message.ClientMessageId))
            return;

        _outgoingVms[message.ClientMessageId] = CreateOutgoingView(message, isNew: !message.IsRestored);
        if (message.IsRestored)
        {
            SyncOutgoingRows();
            return;
        }

        // Your own message is always brought into view, older history makes way for the newest messages
        if (HasNewerMessages)
        {
            _ = GoToLatestAsync();
            return;
        }

        SyncOutgoingRows();
        ScrollToEnd();
    }

    private void OnOutgoingRemoved(ChatOutgoingMessage message)
    {
        if (IsOfThisChat(message) && _outgoingVms.Remove(message.ClientMessageId, out var messageVm))
            HideOutgoing(messageVm);
    }

    private void OnOutgoingSent(ChatOutgoingMessage message, ChatMessageModel? stored)
    {
        if (_isDisposed || !IsOfThisChat(message))
            return;

        // The notification may still be on its way; the stored message takes the row now, and the notification
        // finds it loaded. While older history is shown, the newest messages come when the user goes to them
        if (stored != null && !HasNewerMessages && !stored.IsDeleted && !_deletedMessages.ContainsKey(stored.Id))
            AddMessages([stored], isNew: true);

        // An older server sends nothing back: its notification of the message is in the list already
        if (_outgoingVms.Remove(message.ClientMessageId, out var messageVm))
            HideOutgoing(messageVm);
    }

    private void HideOutgoing(ChatMessageVM messageVm)
    {
        messageVm.DetachOutgoing();
        if (!_shownOutgoing.Remove(messageVm))
            return;

        _timeline.Remove(messageVm);
        this.RaisePropertyChanged(nameof(HasMessages));
    }

    /// <summary>
    /// Shows the messages on their way when the list reaches the newest messages, and takes them out while it shows
    /// older history. While the first page loads, the placeholder covers the list and they wait for the history,
    /// so they come after it rather than alone above the placeholder.
    /// </summary>
    private void SyncOutgoingRows()
    {
        if (HasNewerMessages || IsInitialLoading)
        {
            if (_shownOutgoing.Count == 0)
                return;
            foreach (var shown in _shownOutgoing)
                _timeline.Remove(shown);
            _shownOutgoing.Clear();
            this.RaisePropertyChanged(nameof(HasMessages));
            return;
        }

        var hidden = _outgoingVms.Values.Where(messageVm => !_shownOutgoing.Contains(messageVm)).ToList();
        if (hidden.Count == 0)
            return;

        _shownOutgoing.UnionWith(hidden);
        _ = ResolveSendersAsync(hidden);
        foreach (var messageVm in hidden)
            _senders.Apply(messageVm);
        ParseTextsInBackground(hidden);
        _timeline.AddRange(hidden);
        this.RaisePropertyChanged(nameof(HasMessages));
    }

    /// <summary>
    /// Takes the row of the message on its way that a loaded message was stored from, if there is one.
    /// </summary>
    private ChatMessageVM? TakeOutgoingRowOf(ChatMessageModel stored)
    {
        if (stored.ClientMessageId is not { } clientMessageId
            || !_outgoingVms.Remove(clientMessageId, out var outgoing))
            return null;

        outgoing.DetachOutgoing();
        return _shownOutgoing.Remove(outgoing) ? outgoing : null;
    }

    private ChatMessageVM CreateOutgoingView(ChatOutgoingMessage message, bool isNew)
    {
        var outbox = _outbox!;
        return new ChatMessageVM(message.ToModel(), _currentUserId, _setReplyMessageCommand, _jumpToMessageCommand,
            _editMessageCommand, _deleteMessageCommand, _viewProfileCommand, ConfirmOpenLinkAsync, [], [], Composer,
            _openDirectChatCommand)
        {
            Outgoing = message,
            IsDirectChat = _model.Kind == Shared.Enums.ChatKind.Direct,
            IsReactionAllowed = false,
            IsNew = isNew,
            CreateLinkPreview = preview => new ChatLinkPreviewVM(preview, _coordinatorSession, _images)
            {
                ConfirmOpen = ConfirmOpenLinkAsync
            },
            RetryOutgoing = () => outbox.Retry(message),
            CancelOutgoing = () => outbox.Cancel(message),
            TakeBackOutgoing = () =>
            {
                if (outbox.TakeBack(message))
                    Composer.TakeBack(message);
            }
        };
    }

    // --- How the messages are drawn ---

    // The layout chosen in the settings; bubbles are drawn only while the experimental features are on
    private ChatMessageLayout _messageLayout = ChatMessageLayout.List;

    private void AttachMessageLayout(Client.Application.Settings.IAppearanceService? appearance)
    {
        if (appearance == null)
            return;

        // Raised by the settings page, maybe off the UI thread
        void OnLayoutChanged(ChatMessageLayout layout) => Dispatcher.UIThread.Post(() =>
        {
            _messageLayout = layout;
            ApplyMessageLayout();
        });
        void OnExperimentalChanged() => Dispatcher.UIThread.Post(ApplyMessageLayout);
        _subscriptions
            .Add(() => appearance.ChatMessageLayoutChanged += OnLayoutChanged,
                () => appearance.ChatMessageLayoutChanged -= OnLayoutChanged)
            .Add(() => Client.Services.ExperimentalFeatures.Changed += OnExperimentalChanged,
                () => Client.Services.ExperimentalFeatures.Changed -= OnExperimentalChanged);
        _ = LoadMessageLayoutAsync(appearance);
    }

    private void ApplyMessageLayout()
    {
        if (!_isDisposed)
            IsBubbleLayout = _messageLayout == ChatMessageLayout.Bubbles && Client.Services.ExperimentalFeatures.IsEnabled;
    }

    private async System.Threading.Tasks.Task LoadMessageLayoutAsync(Client.Application.Settings.IAppearanceService appearance)
    {
        try
        {
            _messageLayout = await appearance.GetChatMessageLayoutAsync();
            ApplyMessageLayout();
        }
        catch (Exception)
        {
            // The list stays
        }
    }
}
