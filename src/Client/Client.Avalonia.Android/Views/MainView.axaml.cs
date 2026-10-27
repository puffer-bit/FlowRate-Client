using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Sounds;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Core.Push;
using Client.Droid.Platform.Notifications;
using Client.Services;
using Client.ViewModels.Connections;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Main;
using Client.ViewModels.Profile;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Create;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Droid.Views;

/// <summary>
/// The root view. It stands where the main window stands on the desktop, so it answers the same view
/// model: the open session takes the screen, with a placeholder while there is none, and the side
/// panel with the server and its sessions slides in over it as a menu. The connect form, the profile
/// and the settings take the whole screen, and message boxes lie over everything.
/// </summary>
public partial class MainView : UserControl
{
    private readonly MainWindowVM? _viewModel;
    private readonly ChatView _chatView = new();
    private readonly HybridSessionView _hybridView = new();

    // The chat on screen asks for its message boxes itself; the registration follows the chat
    private readonly SerialDisposable _chatDialogs = new();
    private readonly SerialDisposable _hybridDialogs = new();

    // The message boxes on screen, the topmost one last, each with what the back button answers it
    private readonly List<(TaskCompletionSource<Result> Answer, Result BackAnswer)> _messageBoxes = [];

    private ConnectView? _connectView;
    private ProfileView? _profileView;
    private AccountSecurityView? _securityView;
    private readonly List<LoginPromptVM> _loginPrompts = [];
    private SettingsView? _settingsView;
    private CreateSessionView? _createSessionView;
    private readonly IServiceProvider? _services;
    private TopLevel? _topLevel;
    private IInputPane? _inputPane;

    // Where the open keyboard starts, in the coordinates of the top level; null while it is closed
    private double? _keyboardTop;
    private bool _isInitialized;

    // Set while the menu is open because back led to it from a session: back pressed again then
    // goes on to leaving the application instead of returning to the session
    private bool _isDrawerOpenedByBack;

    public MainView()
    {
        InitializeComponent();

        ServerMenu.Dismissed += () => TryCloseServerMenu();
        ChatMenu.Dismissed += () => TryCloseChatMenu();
        SessionMoveMenu.Dismissed += () => TryClose(SessionMoveMenu);
        CategoryMenu.Dismissed += () => TryClose(CategoryMenu);
        StatusMenu.Dismissed += () => TryClose(StatusMenu);
        PeerMenu.Dismissed += () => TryClose(PeerMenu);

        // Shown from here and not from the chat, which ends above the status bar
        SheetLayer.Children.Add(_chatView.DetachMessageMenu());
    }

    /// <param name="services">Where the settings take the services they change.</param>
    public MainView(MainWindowVM viewModel, IServiceProvider services) : this()
    {
        _viewModel = viewModel;
        _services = services;
        DataContext = viewModel;
        _chatView.MenuRequested += OpenDrawer;
        Sessions.ServerMenuRequested += OpenServerMenu;
        Sessions.ChatMenuRequested += OpenChatMenu;
        Sessions.SessionMoveMenuRequested += OpenSessionMoveMenu;
        Sessions.CategoryMenuRequested += OpenCategoryMenu;
        Sessions.SettingsRequested += OpenSettings;
        Sessions.StatusMenuRequested += () => StatusMenu.IsOpen = true;
        Sessions.PeerMenuRequested += OpenPeerMenu;
        _hybridView.PeerMenuRequested += OpenPeerMenu;

        // A tap on the session that is already shown changes nothing in the view model, but still means "go to it"
        Sessions.SessionTapped += () => Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.CurrentContent != null)
                CloseDrawer();
        });

        viewModel.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await ShowMessageBoxAsync(context.Input)));
        viewModel.SessionsList.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await ShowMessageBoxAsync(context.Input)));
        viewModel.ShowConnectWindow.RegisterHandler(async context =>
            context.SetOutput(await ShowConnectAsync(context.Input)));
        viewModel.ShowCreateSessionWindow.RegisterHandler(async context =>
        {
            await ShowCreateSessionAsync(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        viewModel.ShowUserProfileWindow.RegisterHandler(async context =>
        {
            await ShowProfileAsync(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        viewModel.ShowAccountSecurityWindow.RegisterHandler(async context =>
        {
            await ShowAccountSecurityAsync(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        viewModel.AskLogin.RegisterHandler(async context =>
            context.SetOutput(await ShowLoginPromptAsync(context.Input)));

        // The list is filled again after a lost connection, with new peers for the session on screen
        viewModel.SessionsList.Sessions.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.CurrentContent is ActiveHybridSessionVM shown)
                _hybridView.ShowPeers(FindListSession(shown.Model.Id)?.Peers);
        });

        // A session that ends is taken off the screen from the thread its event came on
        viewModel.WatchProperty(nameof(MainWindowVM.CurrentContent), vm => vm.CurrentContent)
            .Subscribe(content => Dispatcher.UIThread.Post(() =>
            {
                ShowCurrentScreen();

                // The session picked in the menu is what the user wants to see
                if (content != null)
                    CloseDrawer();
            }));

        // The menu of the server is about the server we are connected to
        viewModel.ServerTitle.WatchProperty(nameof(ServerTitleVM.IsReady), title => title.IsReady)
            .Where(isReady => !isReady)
            .Subscribe(_ => Dispatcher.UIThread.Post(() => TryCloseServerMenu()));

        // The chat of a tapped notification can be shown once the list of the server has it
        viewModel.SessionsList.WatchProperty(nameof(SessionsListVM.State), list => list.State)
            .Subscribe(_ => Dispatcher.UIThread.Post(() => ShowRequestedChat(isListFresh: true)));
        MainActivity.ChatRequested += () => ShowRequestedChat(isListFresh: false);
        MainActivity.IsInFrontChanged += OnIsInFrontChanged;
        viewModel.ChatMessageReceived += OnChatMessageReceived;
        viewModel.IsWindowActive = MainActivity.IsInFront;
        viewModel.ChatReads.StateChanged += OnChatReadStateChanged;

        ShowCurrentScreen();
    }

    // For a while after leaving the screen the connection is kept, and the server, seeing the user
    // connected, sends no push: what comes then is notified about from here. On the screen the message
    // makes the sound of the application instead, also in the chat that is open; the notification has
    // the sound of the system
    private void OnChatMessageReceived(PushPayload message)
    {
        if (MainActivity.IsInFront)
            _services?.GetService<ISoundService>()?.Play(message.Mention ? SoundEvent.Mention : SoundEvent.Message);
        else
            ChatNotifications.Show(global::Android.App.Application.Context, message,
                _services?.GetService<IMediaCache>());
    }

    // A chat read here or on another device needs its notification no more. Raised on any thread
    private void OnChatReadStateChanged(ChatReadState state)
    {
        if (state.UnreadCount == 0 && _viewModel?.ConnectedCoordinatorId is { } coordinatorId)
            ChatNotifications.Clear(global::Android.App.Application.Context, coordinatorId, state.ChatId);
    }

    // Off the screen the connection is closed after a while, so that the server notifies the device instead
    private void OnIsInFrontChanged(bool isInFront)
    {
        if (_viewModel == null)
            return;

        _ = isInFront ? _viewModel.ResumeConnection() : _viewModel.SuspendConnection();
        _viewModel.IsWindowActive = isInFront;
        UpdateChatOnScreen();
    }

    /// <param name="isListFresh">The list has just changed, so a chat it lacks is not coming.</param>
    private void ShowRequestedChat(bool isListFresh)
    {
        if (_viewModel == null || MainActivity.RequestedChat is not { } chat)
            return;

        if (_viewModel.TryShowChat(chat.CoordinatorInstanceId, chat.ChatId))
        {
            MainActivity.RequestedChat = null;
            return;
        }

        // Another server, or the chat was deleted: the request must not fire on some later connect
        if (isListFresh && _viewModel.SessionsList is { HasSessions: true, IsStale: false })
            MainActivity.RequestedChat = null;
    }

    // What the user is looking at needs no notification, and the one it already has goes away
    private void UpdateChatOnScreen()
    {
        if (MainActivity.IsInFront && _viewModel is { CurrentContent: ActiveChatSessionVM chat, ConnectedCoordinatorId: { } coordinatorId })
        {
            PushMessageHandler.ChatOnScreen = (coordinatorId, chat.Id);
            ChatNotifications.Clear(global::Android.App.Application.Context, coordinatorId, chat.Id);
            return;
        }

        PushMessageHandler.ChatOnScreen = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
            _topLevel.BackRequested += OnBackRequested;

        _inputPane = _topLevel?.InputPane;
        if (_inputPane != null)
            _inputPane.StateChanged += OnInputPaneStateChanged;

        // The view comes back to the tree when the activity is created again; the application is loaded once
        if (!_isInitialized && _viewModel != null)
        {
            _isInitialized = true;
            _ = InitializeAsync(_viewModel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel != null)
            _topLevel.BackRequested -= OnBackRequested;
        if (_inputPane != null)
            _inputPane.StateChanged -= OnInputPaneStateChanged;
        _topLevel = null;
        _inputPane = null;
        _keyboardTop = null;
        MakeRoomForKeyboard();

        base.OnDetachedFromVisualTree(e);
    }

    // The keyboard is drawn over the application and nothing moves out of its way by itself: the
    // screens are squeezed into what is left above it, so the field being typed in stays in sight
    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        _keyboardTop = e.NewState == InputPaneState.Open ? e.EndRect.Y : null;
        MakeRoomForKeyboard();
    }

    // The system changes the padding as its bars come and go, which moves the edge the room is counted from
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PaddingProperty || change.Property == BoundsProperty)
            MakeRoomForKeyboard();
    }

    private void MakeRoomForKeyboard()
    {
        double covered = 0;

        // Counted from where the content ends, not from the edge of the screen: the gesture area
        // under the content is a part of what the keyboard takes
        if (_keyboardTop is { } top && _topLevel != null
            && this.TranslatePoint(new Point(0, Bounds.Height), _topLevel) is { } bottom)
        {
            covered = Math.Max(0, bottom.Y - Padding.Bottom - top);
        }

        if (Root != null && Root.Margin.Bottom != covered)
            Root.Margin = new Thickness(0, 0, 0, covered);
    }

    private async Task InitializeAsync(MainWindowVM viewModel)
    {
        await viewModel.Initialize();

        // There is nothing to see before connecting, so the menu opens on its offer to connect
        if (!viewModel.IsConnected)
            OpenDrawer();
    }

    private void ShowCurrentScreen()
    {
        UpdateChatOnScreen();

        var overlay = (Control?)_connectView ?? (Control?)_securityView ?? (Control?)_profileView
                      ?? (Control?)_createSessionView ?? _settingsView;
        if (!ReferenceEquals(Overlay.Content, overlay))
        {
            // A form that closes leaves the way it came
            Overlay.IsTransitionReversed = overlay == null;
            Overlay.Content = overlay;
        }

        if (_viewModel?.CurrentContent is ActiveChatSessionVM chat)
        {
            if (!ReferenceEquals(_chatView.DataContext, chat))
            {
                _chatView.DataContext = chat;
                _chatDialogs.Disposable = chat.ShowMessageBox.RegisterHandler(async context =>
                    context.SetOutput(await ShowMessageBoxAsync(context.Input)));
            }

            Screen.Content = _chatView;
            Home.IsVisible = false;
            return;
        }

        if (_viewModel?.CurrentContent is ActiveHybridSessionVM hybrid)
        {
            if (!ReferenceEquals(_hybridView.DataContext, hybrid))
            {
                _hybridView.DataContext = hybrid;
                _hybridDialogs.Disposable = hybrid.ShowMessageBox.RegisterHandler(async context =>
                    context.SetOutput(await ShowMessageBoxAsync(context.Input)));
            }

            // Shown since it was joined, so its row is in the list already
            _hybridView.ShowPeers(FindListSession(hybrid.Model.Id)?.Peers);
            Screen.Content = _hybridView;
            Home.IsVisible = false;
            return;
        }

        Screen.Content = null;
        Home.IsVisible = true;
    }

    private void OpenDrawer() => Drawer.Classes.Set("open", true);

    // The settings are built anew each time, so they open on what is saved
    private void OpenSettings()
    {
        if (_settingsView != null || _services == null)
            return;

        _settingsView = new SettingsView(_services, ShowMessageBoxAsync);
        _settingsView.CloseRequested += CloseSettings;
        ShowCurrentScreen();
    }

    private void CloseSettings()
    {
        _settingsView?.Release();
        _settingsView = null;
        ShowCurrentScreen();
    }

    /// <returns>False when the menu was not open.</returns>
    private bool CloseDrawer()
    {
        if (!Drawer.Classes.Contains("open"))
            return false;

        Drawer.Classes.Set("open", false);
        _isDrawerOpenedByBack = false;
        return true;
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e) => OpenDrawer();

    private void OnDrawerBackdropTapped(object? sender, TappedEventArgs e) => CloseDrawer();

    private void OpenServerMenu() => ServerMenu.IsOpen = true;

    /// <returns>False when there was no menu to close.</returns>
    private bool TryCloseServerMenu()
    {
        if (!ServerMenu.IsOpen)
            return false;

        ServerMenu.IsOpen = false;
        return true;
    }

    private void OpenChatMenu(ChatSessionVM chat)
    {
        ChatMenu.DataContext = chat;
        ChatMenu.IsOpen = true;
    }

    /// <returns>False when there was no menu to close.</returns>
    private bool TryCloseChatMenu()
    {
        if (!ChatMenu.IsOpen)
            return false;

        ChatMenu.IsOpen = false;
        return true;
    }

    private void OpenPeerMenu(PeerVM peer)
    {
        PeerMenu.DataContext = peer;
        PeerMenu.IsOpen = true;
    }

    /// <summary>The voice session of the list of sessions with the given id, or null.</summary>
    private HybridSessionVM? FindListSession(int sessionId) => _viewModel?.SessionsList.Sessions
        .OfType<HybridSessionVM>()
        .FirstOrDefault(session => session.Model.Id == sessionId);

    // Watching a screen opens it, so the sheet goes out of its way
    private void OnPeerMenuClick(object? sender, RoutedEventArgs e) => TryClose(PeerMenu);

    private void OpenSessionMoveMenu(ISessionVM session)
    {
        SessionMoveMenu.DataContext = session;
        SessionMoveMenu.IsOpen = true;
    }

    private void OpenCategoryMenu(SessionCategoryVM category)
    {
        CategoryMenu.DataContext = category;
        CategoryMenu.IsOpen = true;
    }

    /// <returns>False when there was no menu to close.</returns>
    private static bool TryClose(BottomSheet sheet)
    {
        if (!sheet.IsOpen)
            return false;

        sheet.IsOpen = false;
        return true;
    }

    private void OnMoveChatClick(object? sender, RoutedEventArgs e)
    {
        TryCloseChatMenu();

        if (ChatMenu.DataContext is ChatSessionVM chat)
            OpenSessionMoveMenu(chat);
    }

    private void OnMoveSessionUpClick(object? sender, RoutedEventArgs e) => MoveSessionBy(-1);

    private void OnMoveSessionDownClick(object? sender, RoutedEventArgs e) => MoveSessionBy(1);

    // The sheet stays open, so a session can be moved a few places in a row
    private void MoveSessionBy(int step)
    {
        if (SessionMoveMenu.DataContext is ISessionVM session && _viewModel != null)
            _ = _viewModel.SessionsList.MoveSessionBy(session, step);
    }

    private void OnMoveTargetClick(object? sender, RoutedEventArgs e)
    {
        TryClose(SessionMoveMenu);

        if (sender is StyledElement { DataContext: MoveTargetVM target } && SessionMoveMenu.DataContext is ISessionVM session)
            target.MoveHereCommand.Execute(session).Subscribe();
    }

    private void OnCategoryCreateSessionClick(object? sender, RoutedEventArgs e)
    {
        TryClose(CategoryMenu);

        if (CategoryMenu.DataContext is SessionCategoryVM category)
            category.CreateSessionCommand.Execute().Subscribe();
    }

    private void OnCategoryRenameClick(object? sender, RoutedEventArgs e)
    {
        TryClose(CategoryMenu);

        if (CategoryMenu.DataContext is SessionCategoryVM category)
            category.BeginRename();
    }

    private void OnCategoryUpClick(object? sender, RoutedEventArgs e) => MoveCategoryBy(-1);

    private void OnCategoryDownClick(object? sender, RoutedEventArgs e) => MoveCategoryBy(1);

    private void MoveCategoryBy(int step)
    {
        if (CategoryMenu.DataContext is SessionCategoryVM category && _viewModel != null)
            _ = _viewModel.SessionsList.MoveCategoryBy(category, step);
    }

    private void OnCategoryDeleteClick(object? sender, RoutedEventArgs e)
    {
        TryClose(CategoryMenu);

        if (CategoryMenu.DataContext is SessionCategoryVM category)
            category.DeleteCommand.Execute().Subscribe();
    }

    // The command of the item sets the status
    private void OnStatusClick(object? sender, RoutedEventArgs e) => TryClose(StatusMenu);

    private void OnCreateCategoryClick(object? sender, RoutedEventArgs e)
    {
        TryCloseServerMenu();
        _viewModel?.ServerTitle.CreateCategoryCommand.Execute().Subscribe();
    }

    private void OnToggleChatMuteClick(object? sender, RoutedEventArgs e)
    {
        TryCloseChatMenu();

        if (ChatMenu.DataContext is ChatSessionVM chat)
            chat.ToggleMuteCommand.Execute(chat.Model.Id).Subscribe();
    }

    private async void OnCopyAddressClick(object? sender, RoutedEventArgs e)
    {
        TryCloseServerMenu();

        if (_viewModel?.ServerTitle.Address is { } address && _topLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(address);
    }

    private void OnCreateSessionClick(object? sender, RoutedEventArgs e)
    {
        TryCloseServerMenu();
        _viewModel?.ServerTitle.CreateSessionCommand.Execute().Subscribe();
    }

    private void OnManageSecurityClick(object? sender, RoutedEventArgs e)
    {
        TryCloseServerMenu();
        _viewModel?.ServerTitle.ManageSecurityCommand.Execute().Subscribe();
    }

    private void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        TryCloseServerMenu();
        _viewModel?.ServerTitle.DisconnectCommand.Execute().Subscribe();
    }

    /// <summary>
    /// Shows the connect form until it is closed: with the session it connected, or with nothing.
    /// </summary>
    private async Task<ICoordinatorSession?> ShowConnectAsync(ConnectWindowVM connect)
    {
        // The form is taken off the screen after the view model is done with closing it
        var closed = new TaskCompletionSource<ICoordinatorSession?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var close = connect.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        using var messageBoxes = connect.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await ShowMessageBoxAsync(context.Input)));
        using var loginPrompts = connect.AskLogin.RegisterHandler(async context =>
            context.SetOutput(await ShowLoginPromptAsync(context.Input)));

        _connectView = new ConnectView { DataContext = connect };
        ShowCurrentScreen();

        try
        {
            return await closed.Task;
        }
        finally
        {
            _connectView = null;
            ShowCurrentScreen();

            // The form is gone, however it was closed: stop connecting in the background
            connect.CancelTasks();
        }
    }

    /// <summary>
    /// Shows the profile of a user over the screen it was asked from, until it is closed.
    /// </summary>
    private async Task ShowProfileAsync(UserProfileWindowVM profile)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new ProfileView { DataContext = profile };

        using var close = profile.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult();
            context.SetOutput(RxVoid.Default);
        });
        using var focus = profile.FocusName.RegisterHandler(context =>
        {
            view.FocusNameInput();
            context.SetOutput(RxVoid.Default);
        });
        using var pickAvatar = profile.AvatarPicker.PickAvatarFile.RegisterHandler(async context =>
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            var files = storage == null
                ? []
                : await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Strings.Get("profile.avatar.pickerTitle"),
                    AllowMultiple = false,
                    FileTypeFilter = [PickedFiles.ImageFileType(Strings.Get("profile.avatar.fileType"))]
                });

            context.SetOutput((await PickedFiles.FromAsync(files)).FirstOrDefault());
        });

        _profileView = view;
        ShowCurrentScreen();

        try
        {
            await closed.Task;
        }
        finally
        {
            _profileView = null;
            ShowCurrentScreen();
        }
    }

    /// <summary>
    /// Shows the form of a new session until it is closed: by the user, or by itself once the session is created.
    /// </summary>
    private async Task ShowCreateSessionAsync(CreateSessionWindowVM createSession)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var close = createSession.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult();
            context.SetOutput(RxVoid.Default);
        });

        _createSessionView = new CreateSessionView { DataContext = createSession };
        ShowCurrentScreen();

        try
        {
            await closed.Task;
        }
        finally
        {
            _createSessionView = null;
            ShowCurrentScreen();
        }
    }

    /// <summary>
    /// Shows what the user logs in with, opened from the menu of the server, until it is closed.
    /// </summary>
    private async Task ShowAccountSecurityAsync(AccountSecurityVM security)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var close = security.CloseWindow.RegisterHandler(context =>
        {
            closed.TrySetResult();
            context.SetOutput(RxVoid.Default);
        });

        _securityView = new AccountSecurityView { DataContext = security };
        ShowCurrentScreen();

        try
        {
            await closed.Task;
        }
        finally
        {
            _securityView = null;
            ShowCurrentScreen();
        }
    }

    /// <summary>Asks for what finishes a login over everything else, like a message box.</summary>
    private async Task<LoginAnswer?> ShowLoginPromptAsync(LoginPromptVM prompt)
    {
        var view = new LoginPromptView { DataContext = prompt };
        var answer = new TaskCompletionSource<LoginAnswer?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var answers = prompt.Answers.Subscribe(given => answer.TrySetResult(given));

        _loginPrompts.Add(prompt);
        DialogLayer.Children.Add(view);

        try
        {
            return await answer.Task;
        }
        finally
        {
            DialogLayer.Children.Remove(view);
            _loginPrompts.Remove(prompt);
        }
    }

    private Task<Result> ShowMessageBoxAsync(MessageBoxVM messageBox) => ShowMessageBoxAsync(messageBox, Result.Cancel);

    /// <param name="backAnswer">What the system back button answers while the box is the topmost one.</param>
    private async Task<Result> ShowMessageBoxAsync(MessageBoxVM messageBox, Result backAnswer)
    {
        var view = new MessageBoxView { DataContext = messageBox };
        var answer = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var buttons = messageBox.Answers.Subscribe(result => answer.TrySetResult(result));

        _messageBoxes.Add((answer, backAnswer));
        DialogLayer.Children.Add(view);

        try
        {
            return await answer.Task;
        }
        finally
        {
            DialogLayer.Children.Remove(view);
            _messageBoxes.RemoveAll(box => ReferenceEquals(box.Answer, answer));
        }
    }

    // The system back button undoes the last step: closes a message box, then the connect form, the
    // profile, the form of a new session, a page of the settings and the settings, the menu of the
    // server, the menu with the sessions the user opened over a chat, the menu of a message. From a
    // chat it then opens the menu with the sessions and keeps the chat under it. With nothing left
    // to close it opens that menu if it is not open, then asks whether to leave the application,
    // and pressed once more it leaves
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (_messageBoxes.Count > 0)
        {
            var (answer, backAnswer) = _messageBoxes[^1];
            answer.TrySetResult(backAnswer);
            e.Handled = true;
            return;
        }

        if (_loginPrompts.Count > 0)
        {
            _loginPrompts[^1].CancelCommand.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        if (_connectView?.DataContext is ConnectWindowVM connect)
        {
            // While it connects the form stays: it closes itself or tells why it could not connect
            if (!connect.IsBusy)
                connect.CloseCommand.Execute().Subscribe();

            e.Handled = true;
            return;
        }

        if (_securityView?.DataContext is AccountSecurityVM security)
        {
            // Back leaves a change first, then the screen; a change being made is waited for
            if (!security.IsBusy)
                (security.IsChanging ? security.BackCommand : security.CloseCommand).Execute().Subscribe();

            e.Handled = true;
            return;
        }

        if (_profileView?.DataContext is UserProfileWindowVM profile)
        {
            // Back leaves the editing first, then the profile; a name being saved is waited for
            if (!profile.IsBusy)
                (profile.IsEditing ? profile.CancelEditCommand : profile.CloseCommand).Execute().Subscribe();

            e.Handled = true;
            return;
        }

        if (_createSessionView?.DataContext is CreateSessionWindowVM createSession)
        {
            // A session being created is waited for: the form closes itself or tells why it could not
            if (!createSession.IsBusy)
                createSession.CloseCommand.Execute().Subscribe();

            e.Handled = true;
            return;
        }

        if (_settingsView != null)
        {
            if (!_settingsView.TryGoBack())
                CloseSettings();

            e.Handled = true;
            return;
        }

        // An image opened over the chat covers everything of the chat
        if (_chatView.TryCloseImageViewer())
        {
            e.Handled = true;
            return;
        }

        // A screen watched over the whole display covers the session
        if (_hybridView.TryCloseFullscreen())
        {
            e.Handled = true;
            return;
        }

        // Over a session the menu the user opened is closed to get back to it. The menu back itself
        // led to stays, and so does the one without a session, see below
        if (TryCloseServerMenu() || TryCloseChatMenu() || TryClose(SessionMoveMenu) || TryClose(CategoryMenu)
            || TryClose(StatusMenu) || TryClose(PeerMenu)
            || (_viewModel?.CurrentContent != null && !_isDrawerOpenedByBack && CloseDrawer()))
        {
            e.Handled = true;
            return;
        }

        if (_viewModel?.CurrentContent != null && !Drawer.Classes.Contains("open"))
        {
            if (!_chatView.TryCloseMenu())
            {
                // Back from a session leads to the menu with the sessions. The session stays shown
                // under it, so closing the menu returns to it
                OpenDrawer();
                _isDrawerOpenedByBack = true;
            }

            e.Handled = true;
            return;
        }

        e.Handled = true;

        // With no session on screen the menu is the first screen of the application: back goes to it
        if (!Drawer.Classes.Contains("open"))
        {
            OpenDrawer();
            return;
        }

        // Nothing is left but the application itself, which is asked about first
        _ = AskToExitAsync();
    }

    // The question takes the place of the dialog the desktop shows on closing its window. Back once
    // more agrees, so two presses in a row leave the application as they do in most others
    private async Task AskToExitAsync()
    {
        var answer = await ShowMessageBoxAsync(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Get("closeApp.question.back"), Strings.Get("closeApp.title")), Result.Yes);

        if (answer == Result.Yes)
            MainActivity.Current?.Finish();
    }
}
