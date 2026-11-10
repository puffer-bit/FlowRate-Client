using Client.Services;
using Client.Avalonia.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Collections;
using ReactiveUI;
using Shared.Enums;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Main;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// An opened chat: its messages, loaded page by page and kept current by revisions
/// (ActiveChatSessionVM.Sync.cs), and the input under them, see <see cref="ChatComposerVM"/>.
/// </summary>
public partial class ActiveChatSessionVM : ReactiveObject, IMainWindowContentVM
{
    public int Id { get; }
    public string Name { get; private set { this.RaiseAndSetIfChanged(ref field, value); } }

    public ChatComposerVM Composer { get; }

    /// <summary>
    /// The first page is being fetched: the list is covered by the loading placeholder.
    /// </summary>
    public bool IsInitialLoading
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasMessages));
            UpdateReading();
        }
    } = true;

    /// <summary>
    /// An older page is being fetched after the user scrolled to the top.
    /// </summary>
    public bool IsLoadingOlder
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasMessages => _messageVms.Count > 0 || IsInitialLoading;

    /// <summary>
    /// Set by the view when the newest messages are out of sight; shows the scroll-down button.
    /// </summary>
    public bool IsScrolledUp
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (!value)
                UnreadBelowCount = 0;
            UpdateReading();
        }
    }

    /// <summary>
    /// Set by the main window: the chat is its content and the window is in front of the user.
    /// Together with the newest messages in sight it means the user reads the chat.
    /// </summary>
    public bool IsOnScreen
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateReading();
        }
    }

    /// <summary>
    /// Messages from others that arrived while the user was reading older ones.
    /// </summary>
    public int UnreadBelowCount
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasUnreadBelow));
        }
    }

    public bool HasUnreadBelow => UnreadBelowCount > 0;

    public ChatStatusBarVM StatusBar { get; }

    /// <summary>
    /// The image opened over the chat, or null.
    /// </summary>
    public ChatImageViewerVM? ImageViewer
    {
        get;
        private set
        {
            var previous = field;
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsImageViewerOpen));
            previous?.Dispose();
        }
    }

    public bool IsImageViewerOpen => ImageViewer != null;

    /// <summary>
    /// False while the coordinator connection is down: the chat stays open but nothing can be sent.
    /// </summary>
    public bool IsConnected
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Composer.IsConnected = value;
            foreach (var message in _messageVms.Values.Concat(PinnedMessages.Select(row => row.Content)))
                message.RefreshReactionAccess();
        }
    }

    // Rows shown by the list (messages and date separators), kept in sync with _messageVms
    private readonly ChatTimeline _timeline = new();
    public AvaloniaList<IMessageListItemVM> Messages => _timeline.Items;

    // Loaded messages by id; touched only on the UI thread
    private readonly Dictionary<int, ChatMessageVM> _messageVms = new();
    private int _newestMessageId;
    private readonly ChatReadTracker _readTracker;
    private readonly ChatSenderDirectory _senders;
    private Task<int>? _loadingOlder;
    private Task? _initialLoad;
    private bool _isDisposed;

    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();
    public Interaction<RxVoid, RxVoid> ScrollToEndInteraction { get; } = new();
    public Interaction<int, RxVoid> ScrollToMessageIndexInteraction { get; } = new();

    /// <summary>
    /// Asks the view where to save a file of a message: it is given the suggested file name and returns
    /// the stream to write into, or null when the user changed their mind.
    /// </summary>
    public Interaction<string, Stream?> PickFileSaveTarget { get; } = new();
    public Interaction<string, bool> OpenFileInteraction { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> ScrollToEndCommand { get; }

    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _setReplyMessageCommand;
    private readonly ReactiveCommand<int, RxVoid> _jumpToMessageCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _editMessageCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _deleteMessageCommand;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;

    private readonly System.Threading.SemaphoreSlim _historyGate = new(1, 1);
    private readonly Dictionary<int, ChatMessageModel> _messageChanges = new();
    private readonly Dictionary<int, long> _deletedMessages = new();
    public bool HasNewerMessages => _pager.HasNewer;
    public bool IsLoadingNewer
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }
    private void HistoryBoundsChanged()
    {
        this.RaisePropertyChanged(nameof(HasNewerMessages));
        if (HasNewerMessages) IsScrolledUp = true;
        UpdateReading();
    }

    private readonly ICoordinatorSession _coordinatorSession;
    // The chat as last sent, for its permission overrides
    private ChatSessionModel _model;
    private readonly IChatImageService _images;
    private readonly IMediaCache _mediaCache;
    private readonly IDismissedPromptsRepository _dismissedPrompts;
    private readonly EventSubscriptions _subscriptions = new();
    private readonly int _currentUserId;
    private const string OpenLinkPromptKey = "chat.openLink";
    // A longer address is cut in the question, so the box stays on the screen
    private const int OpenLinkPromptMaxLength = 300;

    public ActiveChatSessionVM(ChatSessionModel sessionModel, ICoordinatorSession coordinatorSession,
        ChatSessionServices services, ChatReadTracker readTracker,
        ReactiveCommand<int, RxVoid> viewProfileCommand,
        ReactiveCommand<(int UserId, string Name), RxVoid>? banUserCommand = null,
        ReactiveCommand<int, RxVoid>? unbanUserCommand = null)
    {
        _dismissedPrompts = services.DismissedPrompts;
        _readTracker = readTracker;
        _coordinatorSession = coordinatorSession;
        _images = services.Images;
        _mediaCache = services.MediaCache;
        _viewProfileCommand = viewProfileCommand;
        _model = sessionModel;
        Id = sessionModel.Id;
        Name = sessionModel.Name;
        _ = UpdateChatNameAsync(sessionModel);
        var currentUser = coordinatorSession.GetCurrentUser();
        _currentUserId = currentUser.Id;
        _senders = new ChatSenderDirectory(coordinatorSession, currentUser.Id);
        _senders.Changed += ApplySenders;
        _cache = services.Caches.Open(currentUser.CoordinatorInstanceId, Id, currentUser.Id);
        _pager = new ChatHistoryPager(coordinatorSession, _cache, Id);
        Composer = new ChatComposerVM(Id, Name, sessionModel.IsAttachmentsAllowed, coordinatorSession, currentUser.Id,
            services.MediaUploader, services.Images, _senders, JumpToMessage);

        ApplyAccess(coordinatorSession.Permissions);
        IsConnected = coordinatorSession.ConnectionStatus == CoordinatorState.Connected;

        _setReplyMessageCommand = ReactiveCommand.Create<ChatMessageModel>(Composer.SetReply);
        _jumpToMessageCommand = ReactiveCommand.CreateFromTask<int>(JumpToMessage);
        _editMessageCommand = ReactiveCommand.Create<ChatMessageModel>(Composer.StartEdit);
        // The message leaves the list once the server broadcasts the deletion, like for any other client
        _deleteMessageCommand = ReactiveCommand.CreateFromTask<ChatMessageModel>(
            message => _coordinatorSession.DeleteChatMessage(message.ChatId, message.Id));
        ScrollToEndCommand = ReactiveCommand.CreateFromTask(GoToLatestAsync);

        StatusBar = new ChatStatusBarVM(sessionModel)
        {
            Viewers = new ChatViewersVM(Id, coordinatorSession, viewProfileCommand, Composer, banUserCommand, _currentUserId, unbanUserCommand,
                services.Appearance)
        };
        this.WatchProperties(nameof(IsInitialLoading), nameof(IsLoadingOlder), nameof(IsLoadingNewer))
            .Select(vm => vm.IsInitialLoading || vm.IsLoadingOlder || vm.IsLoadingNewer)
            .Subscribe(loading => StatusBar.IsLoading = loading);

        // Subscribe before the first page so nothing sent meanwhile is missed; duplicates are dropped by id
        _subscriptions
            .Add(() => StatusBar.PropertyChanged += OnPinsPanelChanged,
                () => StatusBar.PropertyChanged -= OnPinsPanelChanged)
            .Add(() => Strings.LanguageChanged += PublishPins, () => Strings.LanguageChanged -= PublishPins)
            .Add(() => Strings.LanguageChanged += StatusBar.UpdateTexts, () => Strings.LanguageChanged -= StatusBar.UpdateTexts)
            .Add(() => coordinatorSession.ChatUserTyping += OnChatUserTyping,
                () => coordinatorSession.ChatUserTyping -= OnChatUserTyping)
            .Add(() => coordinatorSession.TextMessageAdded += OnTextMessageAdded,
                () => coordinatorSession.TextMessageAdded -= OnTextMessageAdded)
            .Add(() => coordinatorSession.TextMessageEdited += OnTextMessageEdited,
                () => coordinatorSession.TextMessageEdited -= OnTextMessageEdited)
            .Add(() => coordinatorSession.TextMessageDeleted += OnTextMessageDeleted,
                () => coordinatorSession.TextMessageDeleted -= OnTextMessageDeleted)
            .Add(() => coordinatorSession.StateChanged += OnCoordinatorStateChanged,
                () => coordinatorSession.StateChanged -= OnCoordinatorStateChanged)
            .Add(() => coordinatorSession.PermissionsChanged += OnPermissionsChanged,
                () => coordinatorSession.PermissionsChanged -= OnPermissionsChanged)
            .Add(() => coordinatorSession.ChatSessionReconfigured += OnChatSessionReconfigured,
                () => coordinatorSession.ChatSessionReconfigured -= OnChatSessionReconfigured)
            .Add(() => readTracker.StateChanged += OnReadStateChanged, () => readTracker.StateChanged -= OnReadStateChanged);

        RestoreTyping();

    }

    /// <summary>
    /// Starts history loading once, after navigation has displayed the chat.
    /// Called on the UI thread; creating the view model does not start I/O.
    /// </summary>
    public Task EnsureLoadedAsync() => _isDisposed
        ? Task.CompletedTask
        : _initialLoad ??= LoadInitialPage(fromCache: true);

    private void OpenImage(ChatImageVM image) =>
        ImageViewer = new ChatImageViewerVM(image, _coordinatorSession, _images, CloseImageViewer);

    public void CloseImageViewer() => ImageViewer = null;

    private IReadOnlyList<ChatImageVM> CreateImages(ChatMessageModel model) =>
        model.Images.Count == 0
            ? []
            : model.Images.Select(image => new ChatImageVM(image, model.Images.Count == 1, _coordinatorSession, _images, OpenImage))
            .ToList();

    private IReadOnlyList<ChatFileVM> CreateFiles(ChatMessageModel model) =>
        model.Files.Count == 0
            ? []
            : model.Files.Select(file => new ChatFileVM(file, _coordinatorSession,
                async fileName => await PickFileSaveTarget.Handle(fileName), _mediaCache,
                async path => await OpenFileInteraction.Handle(path))).ToList();

    /// <summary>
    /// Loads the page before the oldest loaded message and returns the number of messages added.
    /// </summary>
    public Task<int> LoadOlderMessagesAsync()
    {
        if (IsInitialLoading || _pager.IsFullyLoaded)
            return Task.FromResult(0);

        // Scrolling and a reply jump can ask at the same time; both wait for the same page
        if (_loadingOlder == null)
        {
            var loading = LoadOlderPage();
            if (!loading.IsCompleted)
                _loadingOlder = loading;
            return loading;
        }

        return _loadingOlder;
    }

    private async Task<int> LoadOlderPage()
    {
        await _historyGate.WaitAsync();
        IsLoadingOlder = true;
        try
        {
            if (_isDisposed) return 0;
            var page = await _pager.LoadOlderAsync(_pager.IsHistoricalWindow ? _pager.OldestMessageId
                : _messageVms.Count > 0 ? _messageVms.Keys.Min() : null);
            if (page == null)
                return 0;

            int added = 0;
            await UiThread.RunAsync(() =>
            {
                if (!_isDisposed) added = AddMessages(CurrentPage(page.Messages));
            });
            return added;
        }
        finally
        {
            IsLoadingOlder = false;
            _loadingOlder = null;
            _historyGate.Release();
        }
    }

    /// <summary>
    /// Applies the current state of messages by their ids with local removals and batched inserts.
    /// </summary>
    private void ApplyChanges(IReadOnlyList<ChatMessageModel> changes)
    {
        if (changes.Count == 0) return;
        foreach (var change in changes)
        {
            if (change.IsDeleted) _deletedMessages[change.Id] = change.Revision;
            else if (!_messageChanges.TryGetValue(change.Id, out var previous) || change.Revision > previous.Revision)
                _messageChanges[change.Id] = change;
        }
        ApplyPinnedChanges(changes);

        // Changes come in the order of their revisions, so a later state of a message replaces an earlier one
        var latest = new Dictionary<int, ChatMessageModel>();
        foreach (var message in changes)
            latest[message.Id] = message;

        // The messages themselves may be older than what is loaded, while replies to them are on screen.
        // Quotes of a removed message stay in place but can no longer lead to it
        foreach (var messageVm in _messageVms.Values.Concat(PinnedMessages.Select(row => row.Content)))
        {
            if (messageVm.Reply is not { } reply || !latest.TryGetValue(reply.MessageId, out var quoted))
                continue;

            if (quoted.IsDeleted)
                reply.IsDeleted = true;
            else
                reply.Update(quoted);
        }

        int? oldestLoaded = _messageVms.Count > 0 ? _messageVms.Keys.Min() : null;
        var added = new List<ChatMessageModel>();
        var edited = new List<ChatMessageVM>();
        bool removed = false;
        foreach (var message in latest.Values)
        {
            if (!message.IsDeleted && _deletedMessages.ContainsKey(message.Id)) continue;
            if (message.IsDeleted)
            {
                Composer.OnMessageRemoved(message.Id);
                removed |= RemoveLoadedMessage(message.Id);
                continue;
            }

            Composer.OnMessageChanged(message);

            if (_messageVms.TryGetValue(message.Id, out var loaded))
            {
                // A notification can arrive after a page that already had a newer state
                if (message.Revision > loaded.Message.Revision)
                {
                    loaded.Message = message;
                    edited.Add(loaded);
                }
                continue;
            }

            // Only what continues the loaded messages is added; older history comes with its page
            if ((!HasNewerMessages && (_pager.IsFullyLoaded || oldestLoaded == null || message.Id > oldestLoaded))
                || (oldestLoaded != null && message.Id >= oldestLoaded && message.Id <= _newestMessageId))
                added.Add(message);
        }

        if (edited.Count > 0)
        {
            // The edits may mention someone not looked up yet
            _ = ResolveSendersAsync(edited);
            foreach (var messageVm in edited)
                _senders.Apply(messageVm);
        }

        // Adding publishes the count change too; deletion-only batches need their own notification.
        if (AddMessages(added) == 0 && removed)
            this.RaisePropertyChanged(nameof(HasMessages));
    }

    private ChatMessageVM CreateMessageView(ChatMessageModel model, bool isNew = false) =>
        new(model, _currentUserId, _setReplyMessageCommand,
            _jumpToMessageCommand, _editMessageCommand, _deleteMessageCommand, _viewProfileCommand,
            ConfirmOpenLinkAsync, CreateImages(model), CreateFiles(model), Composer)
        {
            IsReactionAllowed = !_model.IsReadOnly && !_model.IsArchived,
            IsPinAllowed = !_model.IsArchived,
            SetPinned = async pinned =>
                (await _coordinatorSession.SetMessagePinned(model.ChatId, model.Id, pinned)).IsSuccess,
            SetReaction = async (emoji, added) =>
                (await _coordinatorSession.SetMessageReaction(model.ChatId, model.Id, emoji, added)).IsSuccess,
            IsNew = isNew,
            CanManageMessages = _coordinatorSession.Permissions.HasIn(_model, Permission.ManageMessages)
        };

    private int AddMessages(IEnumerable<ChatMessageModel> models, bool isNew = false)
    {
        var addedVms = new List<ChatMessageVM>();
        foreach (var model in models)
        {
            if (model.IsDeleted || _deletedMessages.ContainsKey(model.Id)) continue;
            if (_messageVms.TryGetValue(model.Id, out var existing))
            {
                if (model.Revision > existing.Message.Revision)
                {
                    existing.Message = model;
                    _senders.Apply(existing);
                }
                continue;
            }

            var messageVm = CreateMessageView(model, isNew);
            _messageVms.Add(model.Id, messageVm);
            _newestMessageId = Math.Max(_newestMessageId, model.Id);
            addedVms.Add(messageVm);
        }

        if (addedVms.Count == 0)
            return 0;

        // Started first, so the authors it looks up show no name instead of an unknown one
        _ = ResolveSendersAsync(addedVms);
        foreach (var messageVm in addedVms)
            _senders.Apply(messageVm);

        _timeline.AddRange(addedVms);
        this.RaisePropertyChanged(nameof(HasMessages));
        UpdateReading();
        return addedVms.Count;
    }

    /// <summary>
    /// Tells the tracker whether the user reads the chat now, and when so, that everything
    /// loaded is read, provided the window reaches the live end.
    /// </summary>
    private void UpdateReading()
    {
        // Called by setters while the constructor still runs
        if (_readTracker == null)
            return;

        bool isReading = IsOnScreen && !IsScrolledUp && !IsInitialLoading && !HasNewerMessages;
        _readTracker.SetReading(Id, isReading);
        if (isReading && _messageVms.Count > 0)
            _readTracker.MarkRead(Id, _newestMessageId);
    }

    // The counts came from the server, maybe with messages this chat shows already as read
    private void OnReadStateChanged(ChatReadState state)
    {
        if (state.ChatId == Id && state.UnreadCount + state.MentionCount > 0)
            Dispatcher.UIThread.Post(UpdateReading);
    }

    /// <summary>
    /// Looks up the users the given messages show; the others asked for theirs when they were added.
    /// </summary>
    private Task ResolveSendersAsync(IEnumerable<ChatMessageVM> messages)
    {
        var userIds = new HashSet<int>();
        foreach (var messageVm in messages)
            ChatSenderDirectory.CollectUserIds(messageVm, userIds);
        Composer.CollectUserIds(userIds);

        return _senders.ResolveAsync(userIds);
    }

    private void ApplySenders()
    {
        foreach (var messageVm in _messageVms.Values)
            _senders.Apply(messageVm);

        Composer.ApplySenders();
        UpdateTypingNames();
        PublishPins();
    }

    private void RemoveMessage(int messageId)
    {
        // Quotes of the removed message stay in place but can no longer lead to it
        foreach (var messageVm in _messageVms.Values.Concat(PinnedMessages.Select(row => row.Content)))
        {
            if (messageVm.Reply?.MessageId == messageId)
                messageVm.Reply.IsDeleted = true;
        }

        Composer.OnMessageRemoved(messageId);

        if (RemoveLoadedMessage(messageId))
            this.RaisePropertyChanged(nameof(HasMessages));
    }

    private bool RemoveLoadedMessage(int messageId)
    {
        if (!_messageVms.Remove(messageId, out var message))
            return false;

        _timeline.Remove(message);
        if (messageId == _newestMessageId)
            _newestMessageId = _messageVms.Count > 0 ? _messageVms.Keys.Max() : 0;
        return true;
    }

    /// <summary>
    /// Opens a history window around a message and highlights it, without loading intervening pages.
    /// </summary>
    private async Task JumpToMessage(int messageId)
    {
        if (IsInitialLoading || _isDisposed) return;
        if (!_messageVms.ContainsKey(messageId))
        {
            await _historyGate.WaitAsync();
            try
            {
                if (_isDisposed) return;
                var window = await _pager.LoadAroundAsync(messageId);
                if (window == null || _isDisposed) return;
                IsScrolledUp = true;
                ClearHistory();
                HistoryBoundsChanged();
                AddMessages(CurrentPage(window.Messages));
                // Notifications received after the snapshot may already extend its live tail.
                if (!HasNewerMessages)
                    AddMessages(_messageChanges.Values.Where(x => x.Revision > window.Revision
                        && x.Id > _newestMessageId && !_deletedMessages.ContainsKey(x.Id)));
            }
            finally { _historyGate.Release(); }
        }

        if (!_messageVms.TryGetValue(messageId, out var messageVm))
            return;

        int index = Messages.IndexOf(messageVm);
        if (index == -1)
            return;

        await ScrollToMessageIndexInteraction.Handle(index);
        messageVm.Highlight();
    }

    // Asks before a link of a message opens, unless the user chose not to be asked again
    private async Task<bool> ConfirmOpenLinkAsync(Uri link)
    {
        if (await _dismissedPrompts.IsDismissedAsync(OpenLinkPromptKey))
            return true;

        // The address is shown as it will open, which may differ from how the message spells it
        string address = link.ToString();
        if (address.Length > OpenLinkPromptMaxLength)
            address = address[..OpenLinkPromptMaxLength] + "…";

        var question = new MessageBoxVM(Icon.Warning, Buttons.YesNo,
            Strings.Get("chat.openLink.message"), Strings.Get("chat.openLink.title"))
        {
            Detail = address,
            CheckBoxText = Strings.Get("common.dontShowAgain")
        };

        if (await ShowMessageBox.Handle(question) != Result.Yes)
            return false;

        // The check box counts only together with a yes: a refusal is not remembered
        if (question.IsChecked)
            await _dismissedPrompts.SetAsync(OpenLinkPromptKey, true);

        return true;
    }

    private async Task GoToLatestAsync()
    {
        if (HasNewerMessages) await ReloadAsync();
        if (!_isDisposed) ScrollToEnd();
    }

    private void ScrollToEnd() => ScrollToEndInteraction.Handle(RxVoid.Default).Subscribe();

    public void CloseSession()
    {
        Composer.StopTyping();
        StatusBar.Viewers?.Close();
        StatusBar.IsPinsOpen = false;
        ClearTyping();
    }

    // Raised on the transport thread
    private void OnPermissionsChanged(CoordinatorPermissions permissions) =>
        Dispatcher.UIThread.Post(() => ApplyAccess(permissions));

    private async Task UpdateChatNameAsync(ChatSessionModel model)
    {
        if (model.Kind == ChatKind.Direct)
        {
            var otherId = model.MemberIds.FirstOrDefault(id => id != _coordinatorSession.GetCurrentUser().Id);
            if (await _coordinatorSession.GetUser(otherId) is { } other)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_isDisposed && ReferenceEquals(_model, model)) Name = other.DisplayName ?? other.Username;
                });
        }
        else Name = model.Name;
    }

    // Raised on the transport thread
    private void OnChatSessionReconfigured(ChatSessionModel session)
    {
        if (session.Id != Id)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _model = session;
            _ = UpdateChatNameAsync(session);
            StatusBar.ApplySession(session);
            ApplyAccess(_coordinatorSession.Permissions);
        });
    }

    /// <summary>Shows what the current user may do in the chat, after their roles and its overrides.</summary>
    private void ApplyAccess(CoordinatorPermissions permissions)
    {
        var granted = permissions.InSession(_model);
        Composer.CanWrite = granted.HasFlag(Permission.SendMessages);
        foreach (var message in _messageVms.Values.Concat(PinnedMessages.Select(row => row.Content)))
        {
            message.IsReactionAllowed = !_model.IsReadOnly && !_model.IsArchived;
            message.IsPinAllowed = !_model.IsArchived;
            message.RefreshReactionAccess();
        }
        Composer.CanAttachFiles = granted.HasFlag(Permission.AttachFiles);

        var canManage = granted.HasFlag(Permission.ManageMessages);
        foreach (var message in _messageVms.Values.Concat(PinnedMessages.Select(row => row.Content)))
            message.CanManageMessages = canManage;
    }

    public void Dispose()
    {
        _isDisposed = true;
        StatusBar.Dispose();
        ClearTyping();
        _subscriptions.Dispose();
        _senders.Dispose();
        _readTracker.SetReading(Id, false);

        Composer.Dispose();
        ImageViewer = null;
    }
}
