using Client.Droid.Views.Common;
using Client.Droid.Views.Sessions.Chat;
using Client.Droid.Views.Sessions.Hybrid;
using System;
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
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Sounds;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Core.Push;
using Client.Droid.Platform;
using Client.Droid.Platform.Calls;
using Client.Droid.Platform.Notifications;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Droid.Views.Main;

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

    private readonly MainViewNavigation _navigation;
    private readonly IServiceProvider? _services;
    private TopLevel? _topLevel;
    private IInputPane? _inputPane;

    // Where the open keyboard starts, in the coordinates of the top level; null while it is closed
    private double? _keyboardTop;
    private bool _isInitialized;
    private bool _readyForCallActions;
    private bool _handlingCallAction;

    // Set while the menu is open because back led to it from a session: back pressed again then
    // goes on to leaving the application instead of returning to the session
    private bool _isDrawerOpenedByBack;

    public MainView()
    {
        InitializeComponent();
        _navigation = new MainViewNavigation(this, DialogLayer, ShowCurrentScreen,
            () => _viewModel?.CurrentContent as ActiveHybridSessionVM, () => _services);

        ServerMenu.Dismissed += () => TryCloseServerMenu();
        ChatMenu.Dismissed += () => TryCloseChatMenu();
        SessionMoveMenu.Dismissed += () => TryClose(SessionMoveMenu);
        CategoryMenu.Dismissed += () => TryClose(CategoryMenu);
        StatusMenu.Dismissed += () => TryClose(StatusMenu);
        PeerMenu.Dismissed += () => TryClose(PeerMenu);
        PokeMenu.Dismissed += () => TryClose(PokeMenu);
        AfkMenu.Dismissed += () => TryClose(AfkMenu);
        PeerStatsMenu.Dismissed += () => TryClose(PeerStatsMenu);
        PeerStatsMenu.PropertyChanged += (_, e) =>
        {
            if (e.Property == BottomSheet.IsOpenProperty && !PeerStatsMenu.IsOpen)
                PeerStatsMenu.DataContext = null;
        };
        AudioDevicesMenu.Dismissed += () => TryClose(AudioDevicesMenu);

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
        Sessions.SettingsRequested += _navigation.OpenSettings;
        Sessions.StatusMenuRequested += () => StatusMenu.IsOpen = true;
        Sessions.PeerMenuRequested += OpenPeerMenu;
        _hybridView.PeerMenuRequested += OpenPeerMenu;
        _hybridView.MenuRequested += OpenDrawer;
        _hybridView.StatsRequested += _navigation.OpenHybridStats;
        _hybridView.AudioDevicesRequested += () => AudioDevicesMenu.IsOpen = true;

        // A tap on the session that is already shown changes nothing in the view model, but still means "go to it"
        Sessions.SessionTapped += () => Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.CurrentContent != null)
                CloseDrawer();
        });

        viewModel.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await _navigation.ShowMessageBoxAsync(context.Input)));
        viewModel.SessionsList.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await _navigation.ShowMessageBoxAsync(context.Input)));
        viewModel.ShowConnectWindow.RegisterHandler(async context =>
            context.SetOutput(await _navigation.ShowConnectAsync(context.Input)));
        viewModel.ShowCreateSessionWindow.RegisterHandler(async context =>
        {
            await _navigation.ShowCreateSessionAsync(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        viewModel.ShowUserProfileWindow.RegisterHandler(async context =>
        {
            await _navigation.ShowProfileAsync(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        viewModel.ShowAccountSecurityWindow.RegisterHandler(async context =>
        {
            await _navigation.ShowAccountSecurityAsync(context.Input);
            context.SetOutput(RxVoid.Default);
        });
        viewModel.AskLogin.RegisterHandler(async context =>
            context.SetOutput(await _navigation.ShowLoginPromptAsync(context.Input)));

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
        MainActivity.ChatRequested += () =>
        {
            ShowRequestedChat(isListFresh: false);
            _ = HandleRequestedCallAsync();
        };
        MainActivity.IsInFrontChanged += OnIsInFrontChanged;
        viewModel.ChatMessageReceived += OnChatMessageReceived;
        // During the background grace period a live socket can deliver the invitation before FCM.
        viewModel.CallNotificationReceived += payload =>
            IncomingCallNotifications.Handle(global::Android.App.Application.Context, payload);
        viewModel.UsesSystemIncomingCallSound = true;
        PushMessageHandler.IsCallAccountOnScreen = viewModel.IsCallAccountOnScreen;
        viewModel.IsWindowActive = MainActivity.IsInFront;
        viewModel.ChatReads.StateChanged += OnChatReadStateChanged;
        PushMessageHandler.ChatMarkedRead = (coordinatorId, chatId, messageId) => Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.ConnectedCoordinatorId == coordinatorId)
                viewModel.ChatReads.MarkRead(chatId, messageId);
        });

        // The voice session goes on off the screen, with a notification of its own
        _ = new CallTracker(viewModel);

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

    // A chat read on this device needs its notification no more. Raised on any thread
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
        _viewModel.RefreshCallRinging();
        if (isInFront && _viewModel.ConnectedCoordinatorId is { } coordinatorId)
            IncomingCallNotifications.Clear(global::Android.App.Application.Context, coordinatorId);
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

    private async Task HandleRequestedCallAsync()
    {
        if (!_readyForCallActions || _handlingCallAction || _viewModel == null) return;
        _handlingCallAction = true;
        try
        {
            while (MainActivity.RequestedCall is { } call)
            {
                MainActivity.RequestedCall = null;
                await _viewModel.HandlePushCallAsync(call.CoordinatorId, call.UserId, call.ChatId, call.CallId,
                    call.Action == "accept");
            }
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "Handling an incoming call notification failed");
        }
        finally { _handlingCallAction = false; }
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
            // Let the root and its startup screen enter the tree before loading begins.
            Dispatcher.UIThread.Post(() => _ = InitializeAsync(_viewModel), DispatcherPriority.Background);
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
        if (e.NewState != InputPaneState.Open)
            NativeTextMenu.TryDismissActive();
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
        // The root is already visible; the callback also enables the shared development warning.
        await viewModel.Initialize(() => { }, StartupOverlay.FadeOutAsync);
        _readyForCallActions = true;
        await HandleRequestedCallAsync();

        // There is nothing to see before connecting, so the menu opens on its offer to connect
        if (!viewModel.IsConnected)
            OpenDrawer();
    }

    private void ShowCurrentScreen()
    {
        UpdateChatOnScreen();
        if (_viewModel?.CurrentContent is not ActiveHybridSessionVM)
            TryClose(AudioDevicesMenu);

        var overlay = _navigation.GetOverlay();
        if (!ReferenceEquals(Overlay.Content, overlay))
        {
            Overlay.IsTransitionReversed = overlay == null;
            Overlay.Content = overlay;
        }

        if (_viewModel?.CurrentContent is ActiveChatSessionVM chat)
        {
            if (!ReferenceEquals(_chatView.DataContext, chat))
            {
                _chatView.DataContext = chat;
                _chatDialogs.Disposable = chat.ShowMessageBox.RegisterHandler(async context =>
                    context.SetOutput(await _navigation.ShowMessageBoxAsync(context.Input)));
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
                    context.SetOutput(await _navigation.ShowMessageBoxAsync(context.Input)));
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

    private void OnListModeClick(object? sender, RoutedEventArgs e) => TryCloseServerMenu();

    private void OnChatActionClick(object? sender, RoutedEventArgs e) => TryCloseChatMenu();

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

    private void OnPeerPokeClick(object? sender, RoutedEventArgs e)
    {
        if (PeerMenu.DataContext is not PeerVM { CanPoke: true } peer)
            return;
        TryClose(PeerMenu);
        PokeMenu.DataContext = peer;
        PokeMenu.IsOpen = true;
    }

    private void OnPokeSendClick(object? sender, RoutedEventArgs e) => TryClose(PokeMenu);

    private void OnPeerAfkClick(object? sender, RoutedEventArgs e)
    {
        if (PeerMenu.DataContext is not PeerVM { SelfSession: { } session })
            return;
        TryClose(PeerMenu);
        AfkMenu.DataContext = session;
        AfkMenu.IsOpen = true;
    }

    private void OnAfkConfirmClick(object? sender, RoutedEventArgs e) => TryClose(AfkMenu);

    private void OnPeerSelfStatsClick(object? sender, RoutedEventArgs e)
    {
        TryClose(PeerMenu);
        _navigation.OpenHybridStats();
    }

    private void OnPeerStatsClick(object? sender, RoutedEventArgs e)
    {
        if (PeerMenu.DataContext is not PeerVM { ConnectionStats: { } stats })
            return;
        TryClose(PeerMenu);
        PeerStatsMenu.DataContext = stats;
        PeerStatsMenu.IsOpen = true;
    }

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

    // The system back button undoes the last step: closes a message box, then the connect form, the
    // profile, the form of a new session, a page of the settings and the settings, the menu of the
    // server, the menu with the sessions the user opened over a chat, the menu of a message. From a
    // chat it then opens the menu with the sessions and keeps the chat under it. With nothing left
    // to close it opens that menu if it is not open, then asks whether to leave the application,
    // and pressed once more it leaves
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (NativeTextMenu.TryDismissActive())
        {
            e.Handled = true;
            return;
        }

        if (_navigation.TryGoBack())
        {
            e.Handled = true;
            return;
        }

        if (_viewModel?.IsStartupScreenShown == true)
        {
            MainActivity.Current?.FinishAndRemoveTask();
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
            || TryClose(StatusMenu) || TryClose(PeerMenu) || TryClose(PokeMenu) || TryClose(AfkMenu)
            || TryClose(PeerStatsMenu) || TryClose(AudioDevicesMenu)
            || (_viewModel?.CurrentContent != null && !_isDrawerOpenedByBack && CloseDrawer()))
        {
            e.Handled = true;
            return;
        }

        if (_viewModel?.CurrentContent != null && !Drawer.Classes.Contains("open"))
        {
            if (!_chatView.TryCloseMenu() && !_chatView.TryClosePinnedMessages())
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
        var answer = await _navigation.ShowMessageBoxAsync(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Get("closeApp.question.back"), Strings.Get("closeApp.title")), Result.Yes);

        if (answer == Result.Yes)
            MainActivity.Current?.Finish();
    }
}
