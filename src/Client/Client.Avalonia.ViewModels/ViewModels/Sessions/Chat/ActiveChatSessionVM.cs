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
    public string Name { get; }

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
        }
    }

    // Rows shown by the list (messages and date separators), kept in sync with _messageVms
    private readonly ChatTimeline _timeline = new();
    public AvaloniaList<IMessageListItemVM> Messages => _timeline.Items;

    // Loaded messages by id; touched only on the UI thread
    private readonly Dictionary<int, ChatMessageVM> _messageVms = new();
    private readonly ChatReadTracker _readTracker;
    private readonly ChatSenderDirectory _senders;
    private Task<int>? _loadingOlder;

    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();
    public Interaction<RxVoid, RxVoid> ScrollToEndInteraction { get; } = new();
    public Interaction<int, RxVoid> ScrollToMessageIndexInteraction { get; } = new();

    /// <summary>
    /// Asks the view where to save a file of a message: it is given the suggested file name and returns
    /// the stream to write into, or null when the user changed their mind.
    /// </summary>
    public Interaction<string, Stream?> PickFileSaveTarget { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> ScrollToEndCommand { get; }

    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _setReplyMessageCommand;
    private readonly ReactiveCommand<int, RxVoid> _jumpToMessageCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _editMessageCommand;
    private readonly ReactiveCommand<ChatMessageModel, RxVoid> _deleteMessageCommand;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;

    // Upper bound for pages fetched while looking for a replied-to message
    private const int MaxPagesPerJump = 20;

    private readonly ICoordinatorSession _coordinatorSession;
    // The chat as last sent, for its permission overrides
    private ChatSessionModel _model;
    private readonly IChatImageService _images;
    private readonly IDismissedPromptsRepository _dismissedPrompts;
    private readonly EventSubscriptions _subscriptions = new();
    private readonly int _currentUserId;
    private const string OpenLinkPromptKey = "chat.openLink";
    // A longer address is cut in the question, so the box stays on the screen
    private const int OpenLinkPromptMaxLength = 300;

    public ActiveChatSessionVM(ChatSessionModel sessionModel, ICoordinatorSession coordinatorSession,
        ChatSessionServices services, ChatReadTracker readTracker,
        ReactiveCommand<int, RxVoid> viewProfileCommand)
    {
        _dismissedPrompts = services.DismissedPrompts;
        _readTracker = readTracker;
        _coordinatorSession = coordinatorSession;
        _images = services.Images;
        _viewProfileCommand = viewProfileCommand;
        _model = sessionModel;
        Id = sessionModel.Id;
        Name = sessionModel.Name;
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
        ScrollToEndCommand = ReactiveCommand.Create(ScrollToEnd);

        StatusBar = new ChatStatusBarVM(sessionModel);
        this.WatchProperties(nameof(IsInitialLoading), nameof(IsLoadingOlder))
            .Select(vm => vm.IsInitialLoading || vm.IsLoadingOlder)
            .Subscribe(loading => StatusBar.IsLoading = loading);

        // Subscribe before the first page so nothing sent meanwhile is missed; duplicates are dropped by id
        _subscriptions
            .Add(() => Strings.LanguageChanged += StatusBar.UpdateTexts, () => Strings.LanguageChanged -= StatusBar.UpdateTexts)
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

        _ = LoadInitialPage(fromCache: true);
    }

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
                async fileName => await PickFileSaveTarget.Handle(fileName))).ToList();

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
        IsLoadingOlder = true;
        try
        {
            var page = await _pager.LoadOlderAsync(_messageVms.Count > 0 ? _messageVms.Keys.Min() : null);
            if (page == null)
                return 0;

            int added = 0;
            await UiThread.RunAsync(() => added = AddMessages(page.Messages));
            return added;
        }
        finally
        {
            IsLoadingOlder = false;
            _loadingOlder = null;
        }
    }

    /// <summary>
    /// Applies the current state of messages by their ids: removes, replaces or adds them. The list is
    /// rebuilt once for all of them, so a long run of changes after a gap does not rebuild it for each.
    /// </summary>
    private void ApplyChanges(IReadOnlyList<ChatMessageModel> changes)
    {
        if (changes.Count == 0)
            return;

        // Changes come in the order of their revisions, so a later state of a message replaces an earlier one
        var latest = new Dictionary<int, ChatMessageModel>();
        foreach (var message in changes)
            latest[message.Id] = message;

        // The messages themselves may be older than what is loaded, while replies to them are on screen.
        // Quotes of a removed message stay in place but can no longer lead to it
        foreach (var messageVm in _messageVms.Values)
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
            if (message.IsDeleted)
            {
                Composer.OnMessageRemoved(message.Id);
                removed |= _messageVms.Remove(message.Id);
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
            if (_pager.IsFullyLoaded || oldestLoaded == null || message.Id > oldestLoaded)
                added.Add(message);
        }

        if (edited.Count > 0)
        {
            // The edits may mention someone not looked up yet
            _ = ResolveSendersAsync(edited);
            foreach (var messageVm in edited)
                _senders.Apply(messageVm);
        }

        // Adding rebuilds the list, which then takes the removals too
        if (AddMessages(added) == 0 && removed)
            RebuildTimeline();
    }

    private int AddMessages(IEnumerable<ChatMessageModel> models, bool isNew = false)
    {
        var canManage = _coordinatorSession.Permissions.HasIn(_model, Permission.ManageMessages);
        var addedVms = new List<ChatMessageVM>();
        foreach (var model in models)
        {
            if (_messageVms.ContainsKey(model.Id))
                continue;

            var messageVm = new ChatMessageVM(model, _currentUserId, _setReplyMessageCommand,
                _jumpToMessageCommand, _editMessageCommand, _deleteMessageCommand, _viewProfileCommand,
                ConfirmOpenLinkAsync, CreateImages(model), CreateFiles(model))
            {
                IsNew = isNew,
                CanManageMessages = canManage
            };
            _messageVms.Add(model.Id, messageVm);
            addedVms.Add(messageVm);
        }

        if (addedVms.Count == 0)
            return 0;

        // Started first, so the authors it looks up show no name instead of an unknown one
        _ = ResolveSendersAsync(addedVms);
        foreach (var messageVm in addedVms)
            _senders.Apply(messageVm);

        RebuildTimeline();
        UpdateReading();
        return addedVms.Count;
    }

    /// <summary>
    /// Tells the tracker whether the user reads the chat now, and when so, that everything
    /// loaded is read: the loaded messages always end with the newest one.
    /// </summary>
    private void UpdateReading()
    {
        // Called by setters while the constructor still runs
        if (_readTracker == null)
            return;

        bool isReading = IsOnScreen && !IsScrolledUp && !IsInitialLoading;
        _readTracker.SetReading(Id, isReading);
        if (isReading && _messageVms.Count > 0)
            _readTracker.MarkRead(Id, _messageVms.Keys.Max());
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
    }

    private void RemoveMessage(int messageId)
    {
        // Quotes of the removed message stay in place but can no longer lead to it
        foreach (var messageVm in _messageVms.Values)
        {
            if (messageVm.Reply?.MessageId == messageId)
                messageVm.Reply.IsDeleted = true;
        }

        Composer.OnMessageRemoved(messageId);

        if (_messageVms.Remove(messageId))
            RebuildTimeline();
    }

    private void RebuildTimeline()
    {
        _timeline.Update(_messageVms.Values);
        this.RaisePropertyChanged(nameof(HasMessages));
    }

    /// <summary>
    /// Scrolls to a message and highlights it, fetching older pages first when it is not loaded yet.
    /// </summary>
    private async Task JumpToMessage(int messageId)
    {
        for (int page = 0; !_messageVms.ContainsKey(messageId) && page < MaxPagesPerJump; page++)
        {
            if (_pager.IsFullyLoaded || await LoadOlderMessagesAsync() == 0)
                break;
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

    private void ScrollToEnd() => ScrollToEndInteraction.Handle(RxVoid.Default).Subscribe();

    public void CloseSession()
    {
    }

    // Raised on the transport thread
    private void OnPermissionsChanged(CoordinatorPermissions permissions) =>
        Dispatcher.UIThread.Post(() => ApplyAccess(permissions));

    // Raised on the transport thread
    private void OnChatSessionReconfigured(ChatSessionModel session)
    {
        if (session.Id != Id)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _model = session;
            ApplyAccess(_coordinatorSession.Permissions);
        });
    }

    /// <summary>Shows what the current user may do in the chat, after their roles and its overrides.</summary>
    private void ApplyAccess(CoordinatorPermissions permissions)
    {
        var granted = permissions.InSession(_model);
        Composer.CanWrite = granted.HasFlag(Permission.SendMessages);
        Composer.CanAttachFiles = granted.HasFlag(Permission.AttachFiles);

        var canManage = granted.HasFlag(Permission.ManageMessages);
        foreach (var message in _messageVms.Values)
            message.CanManageMessages = canManage;
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _senders.Dispose();
        _readTracker.SetReading(Id, false);

        Composer.Dispose();
        ImageViewer = null;
    }
}
