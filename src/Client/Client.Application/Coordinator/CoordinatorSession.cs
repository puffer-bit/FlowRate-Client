using Client.Application.Coordinator.Users;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator;

public partial class CoordinatorSession : ICoordinatorSession
{
    public CoordinatorState ConnectionStatus
    {
        get;
        private set
        {
            if (field == value)
                return;

            field = value;
            StateChanged?.Invoke(value);

            // A measurement taken on a connection that is gone says nothing about the next one
            if (value != CoordinatorState.Connected)
                ServerRoundTrip = null;
        }
    } = CoordinatorState.New;

    public TimeSpan? ServerRoundTrip
    {
        get;
        private set
        {
            if (field == value)
                return;

            field = value;
            ServerRoundTripChanged?.Invoke(value);
        }
    }

    public CoordinatorSessionModel? CoordinatorModel { get; private set; }
    
    public event Action<CoordinatorState>? StateChanged;
    public event Action<CoordinatorSessionModel>? CoordinatorReconfigured;
    public event Action<int>? OnlineUsersChanged;
    public event Action<ChatSessionModel>? ChatSessionAdded;
    public event Action<ChatSessionModel>? ChatSessionReconfigured;
    public event Action<int>? ChatSessionDeleted;
    public event Action<HybridSessionModel>? HybridSessionAdded;
    public event Action<HybridSessionModel>? HybridSessionReconfigured;
    public event Action<int>? HybridSessionDeleted;
    public event Action<SessionLayout>? SessionLayoutChanged;
    public event Action<int, ChatMessageModel>? TextMessageAdded;
    public event Action<int, ChatMessageModel>? TextMessageEdited;
    public event Action<int, int, long>? TextMessageDeleted;
    public event Action<ChatReadState>? ChatReadStateChanged;
    
    public event Action<PeerJoinedEvent>? HybridSessionPeerJoined;
    public event Action<PeerLeftEvent>? HybridSessionPeerLeft;
    public event Action<PeerStateChangedEvent>? HybridSessionPeerStateChanged;
    public event Action<PeerMicrophoneMuteChangedEvent>? HybridSessionPeerMicrophoneMuteChanged;
    public event Action<PeerSoundMuteChangedEvent>? HybridSessionPeerSoundMuteChanged;
    public event Action<PeerAfkChangedEvent>? HybridSessionPeerAfkChanged;
    public event Action<PeerPokedEvent>? HybridSessionPeerPoked;
    public event Action<HybridSessionPeerEvent>? HybridSessionUpdated;
    public event Action<HybridSessionNodeEvent>? HybridSessionNodeChanged;
    public event Action<IReadOnlyCollection<UserModel>>? UsersReceived;
    public event Action<UserModel>? UserUpdated;
    public event Action<int>? UserDeleted;
    public event Action<UserModel>? CurrentUserChanged;
    public event Action<TimeSpan?>? ServerRoundTripChanged;

    private string? _password;
    
    private readonly ICoordinatorGateway _gateway;
    private readonly ITrustedCertificatesRepository _trustedCertificates;
    private readonly IConnectivityMonitor? _connectivity;
    private readonly UsersDirectory _usersDirectory;
    private readonly ILogger<CoordinatorSession> _logger;

    public CoordinatorSession(ICoordinatorGateway gateway,
        ITrustedCertificatesRepository trustedCertificates,
        ILoggerFactory loggerFactory,
        IConnectivityMonitor? connectivity = null)
    {
        // DI
        _gateway = gateway;
        _trustedCertificates = trustedCertificates;
        _connectivity = connectivity;
        _logger = loggerFactory.CreateLogger<CoordinatorSession>();
        _usersDirectory = new UsersDirectory(gateway, loggerFactory.CreateLogger<UsersDirectory>());

        // Local
        _gateway.EventReceived += ProcessEvent;
        _gateway.ConnectionLost += OnConnectionLost;
        _usersDirectory.UsersReceived += RaiseUsersReceived;

        if (_connectivity != null)
        {
            _isNetworkAvailable = _connectivity.IsNetworkAvailable;
            _connectivity.Changed += OnNetworkChanged;
        }

        _ = Task.Run(RunConnectionLoopAsync);
    }

    private void RaiseUsersReceived(IReadOnlyCollection<UserModel> users) =>
        UsersReceived?.Invoke(users);
    
    /// <summary>
    /// Returns coordinator name form model. If somehow name contains null? method will return "Undefiled coordinator"
    /// </summary>
    /// <returns></returns>
    public string GetCoordinatorName(bool trimName = true)
    {
        if (CoordinatorModel == null || string.IsNullOrWhiteSpace(CoordinatorModel!.Name))
            return trimName ? "undefined" : "Undefined coordinator";
        
        return trimName ? CoordinatorModel.Name : $"Coordinator {CoordinatorModel.Name}";
    }
    
    public void Dispose()
    {
        _lifetime.Cancel();
        _connectionMessages.Writer.TryComplete();

        if (_connectivity != null)
            _connectivity.Changed -= OnNetworkChanged;

        _gateway.EventReceived -= ProcessEvent;
        _gateway.ConnectionLost -= OnConnectionLost;
        _usersDirectory.UsersReceived -= RaiseUsersReceived;
        _gateway.Dispose();
    }
}
