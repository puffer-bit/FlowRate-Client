using Client.Mobile.Views.Common;
using Client.Mobile.Views.Dialogs;
using Client.Views.Shared.Dialogs;
using Client.Mobile.Views.Sessions.Chat;
using Client.Mobile.Views.Sessions.Hybrid;
using System;
using System.Linq;
using System.Threading;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.Avalonia.Controls.Chat;
using Client.Avalonia.Localization;
using Client.Avalonia.Styles.Animation;
using Client.Core.Chat;
using Client.Core.Push;
using Client.Mobile.Platform;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Mobile.Views.Main;

/// <summary>
/// The root view. It stands where the main window stands on the desktop, so it answers the same view
/// model: the home screen holds the server, its chats or sessions and the voice session we are in, and
/// the open session slides in over it. The connect form, the profile and the settings take the whole
/// screen, and message boxes lie over everything.
/// </summary>
public partial class MainView : UserControl
{
    private readonly MainWindowVM? _viewModel;
    // Made anew for every other chat that is opened: a view that showed one chat kept rows of it in its
    // list, and they came up over the next chat. The same chat opened again keeps its view
    private ChatView _chatView;
    private bool _isChatViewUsed;
    private Func<Task<string>>? _quickReaction;
    private readonly HybridSessionView _hybridView = new();

    // The chat on screen asks for its message boxes itself; the registration follows the chat
    private readonly SerialDisposable _chatDialogs = new();
    private readonly SerialDisposable _hybridDialogs = new();

    private readonly MainViewNavigation _navigation;
    private readonly IServiceProvider? _services;
    private TopLevel? _topLevel;
    private IInputPane? _inputPane;
    private IInsetsManager? _insets;
    private IActivatableLifetime? _activatable;

    // Where the open keyboard starts, in the coordinates of the top level; null while it is closed
    private double? _keyboardTop;
    // The application is behind another one, from leaving it until coming back
    private bool _isInBackground;
    // What the application keeps above the keyboard at least, whatever place the keyboard is said to have
    private const double MinRoomAboveKeyboard = 120;
    private bool _isInitialized;

    // Moves the open session sideways while it comes in or goes out
    private readonly TranslateTransform _screenShift = new();
    private CancellationTokenSource? _screenSlide;
    // Whether a session is on the screen, or on its way there
    private bool _isScreenShown;


    /// <summary>
    /// Raised once the application has loaded under the startup screen, from when actions asked for from
    /// outside, such as answering a call from its notification, can be carried out.
    /// </summary>
    public event Action? ApplicationLoaded;

    /// <summary>Shows the shared dialogs over this view; the host makes it the presenter of the platform.</summary>
    public Client.Views.Shared.Dialogs.IDialogPresenter Dialogs => _navigation.Dialogs;

    public MainView()
    {
        InitializeComponent();
        Screen.RenderTransform = _screenShift;
        _navigation = new MainViewNavigation(this, DialogLayer, ShowCurrentScreen,
            () => _viewModel?.CurrentContent as ActiveHybridSessionVM, () => _services);

        ServerMenu.Dismissed += () => TryCloseServerMenu();
        ChatMenu.Dismissed += () => TryCloseChatMenu();
        HybridSessionMenu.Dismissed += () => TryClose(HybridSessionMenu);
        SessionMoveMenu.Dismissed += () => TryClose(SessionMoveMenu);
        ChatMuteMenu.Dismissed += () => TryClose(ChatMuteMenu);
        CategoryMenu.Dismissed += () => TryClose(CategoryMenu);
        StatusMenu.Dismissed += () => TryClose(StatusMenu);
        PeerMenu.Dismissed += () => TryClose(PeerMenu);
        PokeMenu.Dismissed += () => TryClose(PokeMenu);
        AfkMenu.Dismissed += () => TryClose(AfkMenu);
        CallBar.AfkReasonRequested += OpenAfkMenu;
        PeerStatsMenu.Dismissed += () => TryClose(PeerStatsMenu);
        PeerStatsMenu.PropertyChanged += (_, e) =>
        {
            if (e.Property == PopupMenu.IsOpenProperty && !PeerStatsMenu.IsOpen)
                PeerStatsMenu.DataContext = null;
        };
        AudioDevicesMenu.Dismissed += () => TryClose(AudioDevicesMenu);
        SharedMenu.Dismissed += () => TryClose(SharedMenu);
        PopupMenus.Host = SharedMenu;
        // Menus open where they were asked for
        PopupMenu.TrackPresses(this);

        _chatView = AddChatView();
    }

    private ChatView AddChatView()
    {
        // Null and not left unset: the chat view would take the main view model from the tree
        var view = new ChatView { DataContext = null, IsVisible = false, QuickReaction = _quickReaction };
        view.BackRequested += OnChatBackRequested;

        // Shown over the session and the home screen
        SheetLayer.Children.Add(view.DetachMessageMenu());
        // Under the view of a voice session
        Screen.Children.Insert(0, view);
        return view;
    }

    private void ReplaceChatView()
    {
        _chatView.BackRequested -= OnChatBackRequested;
        _chatView.DataContext = null;
        Screen.Children.Remove(_chatView);
        SheetLayer.Children.Remove(_chatView.DetachMessageMenu());

        _chatView = AddChatView();
        _isChatViewUsed = false;
    }

    private void OnChatBackRequested() => _viewModel?.ShowHome();

    /// <param name="services">Where the settings take the services they change.</param>
    public MainView(MainWindowVM viewModel, IServiceProvider services) : this()
    {
        _viewModel = viewModel;
        _services = services;
        DataContext = viewModel;
        var behavior = services.GetRequiredService<IBehaviorService>();
        _quickReaction = async () => (await behavior.GetSettingsAsync()).QuickReaction;
        _chatView.QuickReaction = _quickReaction;
        Sessions.ChatMenuRequested += OpenChatMenu;
        HomeChats.ChatMenuRequested += OpenChatMenu;
        HomeChats.ChatRequested += chat => viewModel.SessionsList.SelectedSession = chat;
        Sessions.HybridMenuRequested += OpenHybridMenu;
        Sessions.CategoryMenuRequested += OpenCategoryMenu;
        Sessions.PeerMenuRequested += OpenPeerMenu;
        _hybridView.PeerMenuRequested += OpenPeerMenu;
        _hybridView.BackRequested += viewModel.ShowHome;
        _hybridView.StatsRequested += _navigation.OpenHybridStats;
        _hybridView.AudioDevicesRequested += () => AudioDevicesMenu.IsOpen = true;

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
            .Subscribe(_ => Dispatcher.UIThread.Post(ShowCurrentScreen));

        // The hidden chat view keeps the last chat, so that opening it again costs little; it lets go
        // of a chat that is closed, and of every chat of a connection that is gone
        viewModel.WatchProperty(nameof(MainWindowVM.OpenedSessions), vm => vm.OpenedSessions)
            .Subscribe(opened => Dispatcher.UIThread.Post(() => FollowOpenedSessions(opened)));

        // The menu of the server is about the server we are connected to
        viewModel.ServerTitle.WatchProperty(nameof(ServerTitleVM.IsReady), title => title.IsReady)
            .Where(isReady => !isReady)
            .Subscribe(_ => Dispatcher.UIThread.Post(() => TryCloseServerMenu()));


        ShowCurrentScreen();
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

        // Told of every change of the insets, the keyboard among them, also of one made without animation
        _insets = _topLevel?.InsetsManager;
        if (_insets != null)
            _insets.SafeAreaChanged += OnSafeAreaChanged;

        _activatable = global::Avalonia.Application.Current?.TryGetFeature<IActivatableLifetime>();
        if (_activatable != null)
        {
            _activatable.Activated += OnAppActivated;
            _activatable.Deactivated += OnAppDeactivated;
        }

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
        if (_insets != null)
            _insets.SafeAreaChanged -= OnSafeAreaChanged;
        if (_activatable != null)
        {
            _activatable.Activated -= OnAppActivated;
            _activatable.Deactivated -= OnAppDeactivated;
        }
        _topLevel = null;
        _inputPane = null;
        _insets = null;
        _activatable = null;
        _keyboardTop = null;
        MakeRoomForKeyboard();

        base.OnDetachedFromVisualTree(e);
    }

    // The keyboard is drawn over the application and nothing moves out of its way by itself: the
    // screens are squeezed into what is left above it, so the field being typed in stays in sight
    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (e.NewState != InputPaneState.Open)
            MobilePlatform.Current?.DismissTextMenu();
        _keyboardTop = e.NewState == InputPaneState.Open ? e.EndRect.Y : null;
        MakeRoomForKeyboard();
    }

    // Leaving for another application hides the keyboard without its animation, and the input pane
    // reports a change only with one: the room it took would stay empty at the bottom on return
    private void OnAppDeactivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background)
            return;
        _isInBackground = true;
        _keyboardTop = null;
        MakeRoomForKeyboard();
    }

    // A keyboard that comes back with the app may do it without its animation, which the input pane
    // does not report: the room is worked out again from where the keyboard is
    private void OnAppActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind != ActivationKind.Background)
            return;
        _isInBackground = false;
        Dispatcher.UIThread.Post(MakeRoomForKeyboard, DispatcherPriority.Background);
    }

    // Raised before the input pane takes its new state from the same insets: the room is worked out after it
    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) =>
        Dispatcher.UIThread.Post(MakeRoomForKeyboard);

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
        // The pane may have closed without telling, see OnAppDeactivated
        if (_inputPane is { State: not InputPaneState.Open })
            _keyboardTop = null;
        // The keyboard is open with no animation that told of it, e.g. back with the app from the
        // background: its place is read from the insets
        // Only in front: a window going away or not yet back has no size to count from
        else if (_keyboardTop == null && !_isInBackground && _topLevel is { ClientSize.Height: > 0 } topLevel
                 && _inputPane is { State: InputPaneState.Open, OccludedRect: { Height: > 0 } occluded }
                 && occluded.Y > 0 && occluded.Bottom <= topLevel.ClientSize.Height + 1)
            _keyboardTop = occluded.Y;

        if (_keyboardTop is { } top && _topLevel != null
            && this.TranslatePoint(new Point(0, Bounds.Height), _topLevel) is { } bottom)
        {
            // A keyboard never takes the whole screen: a wrong place must not leave the application without room
            covered = Math.Clamp(bottom.Y - Padding.Bottom - top, 0, Math.Max(0, Bounds.Height - MinRoomAboveKeyboard));
        }

        if (Root != null && Root.Margin.Bottom != covered)
            Root.Margin = new Thickness(0, 0, 0, covered);
    }

    private async Task InitializeAsync(MainWindowVM viewModel)
    {
        // The root is already visible; the callback also enables the shared development warning.
        await viewModel.Initialize(() => { }, StartupOverlay.FadeOutAsync);
        ApplicationLoaded?.Invoke();
    }

    private void ShowCurrentScreen()
    {
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
                if (_isChatViewUsed)
                    ReplaceChatView();
                _isChatViewUsed = true;
                _chatView.DataContext = chat;
                _chatDialogs.Disposable = chat.ShowMessageBox.RegisterHandler(async context =>
                    context.SetOutput(await _navigation.ShowMessageBoxAsync(context.Input)));
            }
            else if (!_chatView.IsVisible)
            {
                // The same chat comes back from the home screen: it opens at its newest message, as
                // any chat does
                _chatView.ScrollToNewest();
            }

            ChatOpenTrace.Mark("chat view bound");
            _chatView.IsVisible = true;
            ShowHybridView(false);
            ShowScreen();
            return;
        }

        if (_viewModel?.CurrentContent is ActiveHybridSessionVM hybrid)
        {
            _chatView.IsVisible = false;

            if (!ReferenceEquals(_hybridView.DataContext, hybrid))
            {
                _hybridView.DataContext = hybrid;
                // A phone switches its cameras while it sends, so there is nothing to choose before
                hybrid.StartsCameraWithoutDialog = true;
                _hybridDialogs.Disposable = hybrid.ShowMessageBox.RegisterHandler(async context =>
                    context.SetOutput(await _navigation.ShowMessageBoxAsync(context.Input)));
            }

            // Shown since it was joined, so its row is in the list already
            _hybridView.ShowPeers(FindListSession(hybrid.Model.Id)?.Peers);
            ShowHybridView(true);
            ShowScreen();
            return;
        }

        // The session slides away with what it showed, and is let go once it is off the screen
        if (_isScreenShown)
        {
            _isScreenShown = false;
            SlideScreen(show: false);
        }
    }

    private void ReleaseChat()
    {
        _chatView.DataContext = null;
        _chatDialogs.Disposable = null;
    }

    private readonly SerialDisposable _openedSessions = new();

    private void FollowOpenedSessions(OpenedSessions? opened)
    {
        if (_chatView.DataContext != null && _viewModel?.CurrentContent != _chatView.DataContext)
            ReleaseChat();

        if (opened == null)
        {
            _openedSessions.Disposable = null;
            return;
        }

        void OnSessionClosed(IMainWindowContentVM session) => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_chatView.DataContext, session) && _viewModel?.CurrentContent != session)
                ReleaseChat();
        });

        opened.SessionClosed += OnSessionClosed;
        _openedSessions.Disposable = Disposable.Create(() => opened.SessionClosed -= OnSessionClosed);
    }

    // The view of a voice session is in the tree only while it is shown, as it always was
    private void ShowHybridView(bool show)
    {
        if (show && !Screen.Children.Contains(_hybridView))
            Screen.Children.Add(_hybridView);
        else if (!show)
            Screen.Children.Remove(_hybridView);
    }

    private void ShowScreen()
    {
        if (_isScreenShown)
            return;

        _isScreenShown = true;
        SlideScreen(show: true);
    }

    // A session comes in from the right over the home screen, and goes back out to the right; from
    // where a swipe left it, too
    private async void SlideScreen(bool show)
    {
        // A slide the other way that is still running turns around from where it got to
        var current = _screenShift.X;
        _screenSlide?.Cancel();
        var slide = _screenSlide = new CancellationTokenSource();
        var width = Root.Bounds.Width;

        Home.IsVisible = true;
        if (show && !Screen.IsVisible)
            current = width;
        Screen.IsVisible = true;
        _screenShift.X = current;

        var target = show ? 0 : width;
        var duration = Motion.Get(MotionDuration.Long);
        if (duration > TimeSpan.Zero && width > 0 && current != target
            && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            // Moved frame by frame here rather than by an animation: an animation lets go of its value
            // when it ends, and the screen would jump back to where it started for a frame
            var ease = Motion.Ease(MotionEase.Standard);
            var finished = new TaskCompletionSource();
            TimeSpan? start = null;
            void Step(TimeSpan time)
            {
                if (slide.IsCancellationRequested)
                {
                    finished.TrySetResult();
                    return;
                }

                start ??= time;
                var progress = Math.Clamp((time - start.Value) / duration, 0, 1);
                _screenShift.X = current + (target - current) * ease.Ease(progress);
                if (progress < 1)
                    topLevel.RequestAnimationFrame(Step);
                else
                    finished.TrySetResult();
            }

            topLevel.RequestAnimationFrame(Step);
            await finished.Task;
            if (slide.IsCancellationRequested)
                return;
        }

        _screenShift.X = 0;
        if (show)
        {
            Home.IsVisible = false;
            return;
        }

        Screen.IsVisible = false;
        _chatView.IsVisible = false;
        ShowHybridView(false);
    }

    // The menu of the server we are connected to, or the connect form while there is none
    private void OnServerTitleClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        if (_viewModel.ServerTitle.IsReady)
            OpenServerMenu();
        else if (!_viewModel.IsConnected)
            _viewModel.ConnectCommand.Execute().Subscribe();
    }

    private void OnProfileClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is { IsConnected: true, UserCard.UserId: { } userId })
            _viewModel.UserCard.ViewProfileCommand.Execute(userId).Subscribe();
    }

    // Opened where its item was, once the menu of the home screen is gone
    private void OnStatusMenuClick(object? sender, RoutedEventArgs e) => StatusMenu.IsOpen = true;

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => _navigation.OpenSettings();

    private void OnNotificationsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            _navigation.OpenNotifications(_viewModel.Notifications);
    }

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

    // Watching a screen opens it, so the menu goes out of its way
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

    // A poke without a message, at once
    private void OnPeerPokeNowClick(object? sender, RoutedEventArgs e)
    {
        if (PeerMenu.DataContext is not PeerVM { CanPoke: true } peer)
            return;
        TryClose(PeerMenu);
        peer.PokeMessage = string.Empty;
        peer.Poke();
    }

    private void OnPeerAfkClick(object? sender, RoutedEventArgs e)
    {
        if (PeerMenu.DataContext is not PeerVM { SelfSession: { } session })
            return;
        TryClose(PeerMenu);
        OpenAfkMenu(session);
    }

    // The reason to go away with, chosen from the presets or written
    private void OpenAfkMenu(HybridSessionStatusBarVM session)
    {
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

    private void OpenHybridMenu(HybridSessionVM session)
    {
        HybridSessionMenu.DataContext = session;
        HybridSessionMenu.IsOpen = true;
    }

    // The command of the item acts on the session
    private void OnHybridActionClick(object? sender, RoutedEventArgs e) => TryClose(HybridSessionMenu);

    private void OnHybridJoinClick(object? sender, RoutedEventArgs e)
    {
        TryClose(HybridSessionMenu);

        if (HybridSessionMenu.DataContext is HybridSessionVM session)
            session.Connect();
    }

    private void OnMoveHybridClick(object? sender, RoutedEventArgs e)
    {
        TryClose(HybridSessionMenu);

        if (HybridSessionMenu.DataContext is HybridSessionVM session)
            OpenSessionMoveMenu(session);
    }

    private void OpenCategoryMenu(SessionCategoryVM category)
    {
        CategoryMenu.DataContext = category;
        CategoryMenu.IsOpen = true;
    }

    /// <returns>False when there was no menu to close.</returns>
    private static bool TryClose(PopupMenu menu)
    {
        if (!menu.IsOpen)
            return false;

        menu.IsOpen = false;
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

    // The menu stays open, so a session can be moved a few places in a row
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

    // The menu of the chat gives way to the choice of how long it stays muted
    private void OnMuteChatClick(object? sender, RoutedEventArgs e)
    {
        var chat = ChatMenu.DataContext as ChatSessionVM;
        TryCloseChatMenu();

        if (chat == null)
            return;
        ChatMuteMenu.DataContext = chat;
        ChatMuteMenu.IsOpen = true;
    }

    // The command of the item mutes the chat
    private void OnChatMuteChoiceClick(object? sender, RoutedEventArgs e) => TryClose(ChatMuteMenu);

    private void OnToggleChatMuteClick(object? sender, RoutedEventArgs e)
    {
        TryCloseChatMenu();

        if (ChatMenu.DataContext is ChatSessionVM chat)
            chat.ToggleMuteCommand.Execute(chat.Model.Id).Subscribe();
    }

    // The QR code of a link that fills in the connect form on another device; its share button sends the link on
    private void OnShareServerLinkClick(object? sender, RoutedEventArgs e)
    {
        TryCloseServerMenu();

        if (_viewModel?.ServerTitle is { HasServerLink: true } title && DialogPresenter.Current is { } dialogs)
            _ = dialogs.ShowAsync<object?>(new ServerLinkDialog { DataContext = title }, this, null);
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

    // Back dismisses overlays first, then returns from a session to the personal chat list.
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (MobilePlatform.Current?.DismissTextMenu() == true)
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
            MobilePlatform.Current?.Exit(removeFromRecents: true);
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

        // Menus close before navigating away from the current screen.
        if (TryCloseServerMenu() || TryCloseChatMenu() || TryClose(HybridSessionMenu) || TryClose(SessionMoveMenu)
            || TryClose(CategoryMenu)
            || TryClose(StatusMenu) || TryClose(PeerMenu) || TryClose(PokeMenu) || TryClose(AfkMenu)
            || TryClose(PeerStatsMenu) || TryClose(AudioDevicesMenu) || TryClose(SharedMenu))
        {
            e.Handled = true;
            return;
        }

        if (_viewModel?.CurrentContent != null)
        {
            if (!_chatView.TryCloseMenu() && !_chatView.TryCloseMessageDetails() && !_chatView.TryCloseViewers()
                && !_chatView.TryClosePinnedMessages())
            {
                _viewModel.ShowHome();
            }

            e.Handled = true;
            return;
        }

        e.Handled = true;

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
            MobilePlatform.Current?.Exit(removeFromRecents: false);
    }
}
