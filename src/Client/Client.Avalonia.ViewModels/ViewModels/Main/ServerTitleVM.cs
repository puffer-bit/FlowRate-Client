using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using ReactiveUI;
using Client.Core.Coordinator;
using Client.Application.Coordinator;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Main;

/// <summary>
/// The server we are connected to, shown at the top of the side panel, with its own menu.
/// </summary>
public class ServerTitleVM : ReactiveObject
{
    private ICoordinatorSession? _coordinatorSession;
    
    public string? Name
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>
    /// The name of the server, or the words that there is none while offline.
    /// </summary>
    public string DisplayName => Name ?? Strings.Get("main.status.notConnected");
    
    /// <summary>
    /// Where the server is, shown under its name.
    /// </summary>
    public string? Address
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(DisplayAddress));
        }
    }

    public string DisplayAddress => Address ?? Strings.Get("main.status.offline");

    public int MaxUsersOnline
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(OnlineText));
        }
    }
    
    public int UsersOnline
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(OnlineText));
        }
    }

    /// <summary>
    /// How many users are connected, out of how many the server lets in at once.
    /// </summary>
    public string OnlineText => MaxUsersOnline == 0 ? $"{UsersOnline} / ∞" : $"{UsersOnline} / {MaxUsersOnline}";

    public string? Platform
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? Version
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsReady
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>What the current user may do; the menu offers only that.</summary>
    public AccessVM Access { get; }

    public ReactiveCommand<RxVoid, RxVoid> CreateSessionCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> CreateCategoryCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> OpenSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> DisconnectCommand { get; }

    public ServerTitleVM(ReactiveCommand<RxVoid, RxVoid> createSessionCommand,
        ReactiveCommand<RxVoid, RxVoid> createCategoryCommand,
        ReactiveCommand<RxVoid, RxVoid> openSettingsCommand,
        ReactiveCommand<RxVoid, RxVoid> disconnectCommand, AccessVM access)
    {
        Access = access;
        OpenSettingsCommand = openSettingsCommand;
        CreateSessionCommand = createSessionCommand;
        CreateCategoryCommand = createCategoryCommand;
        DisconnectCommand = disconnectCommand;

        // The title lives as long as the main window, so the subscription is never dropped
        Strings.LanguageChanged += () =>
        {
            this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(DisplayAddress));
        };
    }

    public async Task AttachCoordinator(ICoordinatorSession coordinatorSession)
    {
        if (_coordinatorSession != null)
        {
            _coordinatorSession.CoordinatorReconfigured -= OnCoordinatorReconfigured;
            _coordinatorSession.OnlineUsersChanged -= OnOnlineUsersChanged;
        }
        _coordinatorSession = coordinatorSession;
        _coordinatorSession.CoordinatorReconfigured += OnCoordinatorReconfigured;
        _coordinatorSession.OnlineUsersChanged += OnOnlineUsersChanged;
        
        if (_coordinatorSession.ConnectionStatus == CoordinatorState.Connected)
            IsReady = true;
        
        MaxUsersOnline = _coordinatorSession.CoordinatorModel?.MaxOnlineUsers ?? 0;
        UsersOnline = _coordinatorSession.CoordinatorModel?.ConnectedUsersCount ?? 0;
        Platform = _coordinatorSession.CoordinatorModel?.ServerPlatform;
        Version = _coordinatorSession.CoordinatorModel?.ServerVersion;
        Name = _coordinatorSession.GetCoordinatorName();
        Address = _coordinatorSession.ConnectedAddress;
    }
    
    public void DetachCoordinator()
    {
        IsReady = false;
        if (_coordinatorSession != null)
        {
            _coordinatorSession.CoordinatorReconfigured -= OnCoordinatorReconfigured;
            _coordinatorSession.OnlineUsersChanged -= OnOnlineUsersChanged;
        }
        _coordinatorSession = null;
        
        MaxUsersOnline = 0;
        UsersOnline = 0;
        Platform = null;
        Version = null;
        Name = null;
        Address = null;
    }

    // A new name or limit, saved from the control panel or by a user, shows at once
    private void OnCoordinatorReconfigured(CoordinatorSessionModel coordinator) => Dispatcher.UIThread.Post(() =>
    {
        if (_coordinatorSession == null)
            return;

        MaxUsersOnline = coordinator.MaxOnlineUsers;
        UsersOnline = coordinator.ConnectedUsersCount;
        Name = _coordinatorSession.GetCoordinatorName();
    });

    private void OnOnlineUsersChanged(int connectedUsersCount) => Dispatcher.UIThread.Post(() =>
    {
        if (_coordinatorSession != null)
            UsersOnline = connectedUsersCount;
    });

    public void Update()
    {
        
    }
}