using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Client.Services;
using Client.Tools.Parsing;
using Client.ViewModels.Dialogs;
using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;
using Shared.Validation;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Connections;

public class ConnectWindowVM : ReactiveObject
{
    // Matches the coordinator port in the server defaults
    public const int DefaultPort = 26666;

    public SavedUserModel? SelectedIdentity
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsIdentityQrAvailable));

            // The server password saved with the identity is filled in; "New*" keeps what was typed
            if (value?.UserIdentity != null)
                Password = value.Password;
        }
    }

    public bool IsIdentitiesSelectable => Identities.Count > 1;
    public bool IsIdentityQrAvailable => !string.IsNullOrEmpty(SelectedIdentity?.UserIdentity);

    // Connecting before the saved identities are listed would go out as "New*"
    public bool IsLoadingIdentities
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsBusy => _isBusy.Value;

    public bool IsPasswordVisible
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string ConnectionStatus
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = Strings.Get("connect.status.waitingForAction");

    /// <summary>
    /// The host, as typed or picked. A port or a scheme typed into it is accepted as well.
    /// </summary>
    public string? ServerAddress
    {
        get => field;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            _isAddressEdited = true;
            ValidateEndpoint();
        }
    }

    /// <summary>
    /// Empty means <see cref="DefaultPort"/>.
    /// </summary>
    public string? Port
    {
        get => field;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            ValidateEndpoint();
        }
    }

    public string PortPlaceholder => DefaultPort.ToString();

    public string? AddressError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? PortError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The port typed into the address field wins over the port field until the address field loses focus.
    /// </summary>
    public string? PortHint
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? PasswordError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<SavedServerItem> SavedServers { get; } = [];

    public SavedServerItem? SelectedSavedServer
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);

            if (value != null)
            {
                Port = value.Port == DefaultPort ? null : value.Port.ToString();
                ServerAddress = value.Host;
            }
            ValidateEndpoint();
        }
    }

    public string? Password
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            PasswordError = value?.Length > Limits.PasswordMaxLength
                ? Strings.Format("connect.password.tooLong", ("limit", Limits.PasswordMaxLength))
                : null;
            IsFormValid = IsAddressValid && PasswordError == null;
        }
    }

    public ObservableCollection<SavedUserModel> Identities { get; } = [];

    public bool IsAddressValid
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsFormValid
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private string? _parsedAddress;
    private int? _parsedPort;
    private int? _addressPort;

    // An empty untouched address is not a mistake, it shows no error until it is first edited
    private bool _isAddressEdited;

    // The server the identities were last looked up for, so typing does not ask again for the same one
    private string? _identitiesEndpoint;

    // Bumped by every lookup, so a slower earlier one does not fill the list after a newer one
    private int _identitiesLookup;

    public Interaction<ICoordinatorSession?, RxVoid> CloseWindow { get; }
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; }

    /// <summary>Asks for the account password or a one-time code when the login needs them.</summary>
    public Interaction<LoginPromptVM, LoginAnswer?> AskLogin { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> ConnectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }
    private ReactiveCommand<RxVoid, RxVoid> FindIdentitiesCommand { get; }

    private readonly ICoordinatorFactory _coordinatorFactory;
    private readonly IAccountingService _accountingService;
    private readonly IBehaviorService _behaviorService;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    public ConnectWindowVM(
        IAccountingService accountingService,
        ICoordinatorFactory coordinatorFactory,
        IBehaviorService behaviorService)
    {
        _accountingService = accountingService;
        _behaviorService = behaviorService;
        _coordinatorFactory = coordinatorFactory;

        CloseWindow = new Interaction<ICoordinatorSession?, RxVoid>();
        ShowMessageBox = new Interaction<MessageBoxVM, Result>();

        ConnectCommand = ReactiveCommand.CreateFromTask(
            ConnectAsync,
            this.WatchProperty(nameof(IsLoadingIdentities), x => !x.IsLoadingIdentities));

        CloseCommand = ReactiveCommand.CreateFromTask(HandleCloseWindow,
            ConnectCommand.IsExecuting.Select(busy => !busy));

        _isBusy = ConnectCommand.IsExecuting.ToProperty(this, nameof(IsBusy));

        FindIdentitiesCommand = ReactiveCommand.CreateFromTask(FindIdentities);

        Task.Run(async () =>
        {
            var savedServers = await _accountingService.GetAllSavedCoordinatorsAsync();

            var lastServer = savedServers.MaxBy(server => server.SaveOrder);

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var server in savedServers)
                    SavedServers.Add(new SavedServerItem(server.CoordinatorId, server.Name, UriParsing.FormatHost(server.Address), server.Port));

                if (!_isAddressEdited && lastServer != null)
                    SelectedSavedServer = SavedServers.FirstOrDefault(s => s.CoordinatorId == lastServer.CoordinatorId);
            });
        });

    }

    private async Task ConnectAsync()
    {
        if (!IsFormValid)
            return;
        var session = _coordinatorFactory.CreateCoordinatorSession();
        LoginPromptVM.Attach(session, AskLogin);

        // Only the reason of a rejected login is needed, the main window takes over the session after
        RequestFailedException? rejection = null;
        void OnRejected(RequestFailedException exception) => rejection = exception;
        session.RequestRejected += OnRejected;

        try
        {
            if (!await ConnectWithCertificateAsync(session))
            {
                session.Dispose();
                return;
            }

            if (session.ConnectionStatus == CoordinatorState.CoordinatorMismatch)
            {
                var mismatch = session.CoordinatorMismatch;
                session.Dispose();
                await ShowMessageBox.Handle(CertificateTrustMessages.CreateMismatchMessage(mismatch));
                return;
            }

            // Given up by the user, who knows it
            if (session.ConnectionStatus == CoordinatorState.AuthenticationNeeded)
            {
                session.Dispose();
                return;
            }

            if (session.ConnectionStatus != CoordinatorState.Connected)
            {
                var message = DescribeConnectionFailure(session.ConnectionStatus, rejection);
                session.Dispose();
                await HandleConnectionError(message);
                return;
            }

            // Set by the successful login above. A saved identity is stored again as well, so it becomes
            // the last one and is offered first next time instead of "New*"
            await SaveCoordinatorAsync(session.CoordinatorModel!, session.GetCurrentUserCredentials(),
                session.AccountPasswordToSave);

            await CloseWindow.Handle(session);
        }
        catch (OperationCanceledException)
        {
            await session.Disconnect();
            session.Dispose();
        }
        catch (TimeoutException)
        {
            await FinalizeConnectionError(session, Strings.Get("connect.failed.timedOut"));
        }
        catch (Exception ex)
        {
            await FinalizeConnectionError(session, Strings.Format("connect.failed.unexpected", ("error", ex.Message)));
        }
        finally
        {
            session.RequestRejected -= OnRejected;
            ConnectionStatus = Strings.Get("connect.status.waitingForAction");
        }
    }

    private async Task<bool> ConnectWithCertificateAsync(ICoordinatorSession session)
    {
        ConnectionStatus = Strings.Get("main.status.waitingForServer");

        string? identity = SelectedIdentity?.UserIdentity;
        var accountPassword = SelectedIdentity?.AccountPassword;
        var sessionToken = SelectedIdentity?.SessionToken;
        var expectedCoordinatorId = SelectedIdentity?.CoordinatorId is { Length: > 0 } id ? id : null;
        await session.Connect(_parsedAddress!, _parsedPort!.Value, identity, Password, accountPassword,
            sessionToken, expectedCoordinatorId);

        _cancellationTokenSource.Token.ThrowIfCancellationRequested();

        if (session.ConnectionStatus == CoordinatorState.CertificateNotTrusted)
        {
            if (!await ConfirmCertificateAsync(session))
                return false;

            ConnectionStatus = Strings.Get("main.status.waitingForServer");
            await session.Connect(_parsedAddress!, _parsedPort!.Value, identity, Password, accountPassword,
                sessionToken, expectedCoordinatorId);

            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
        }

        return true;
    }

    private static string DescribeConnectionFailure(CoordinatorState state, RequestFailedException? rejection) =>
        state switch
        {
            CoordinatorState.AuthenticationFailed => Strings.Get("connect.failed.authFailed"),
            CoordinatorState.Denied => Strings.Get("connect.failed.banned"),
            CoordinatorState.PasskeyRequired => Strings.Get("connect.failed.passkeyRequired"),
            CoordinatorState.ClientOutdated => Strings.Get("connect.failed.clientOutdated"),
            CoordinatorState.ServerOutdated => Strings.Get("connect.failed.serverOutdated"),
            CoordinatorState.Rejected when rejection != null => RequestRejectionMessages.GetForConnection(rejection.Code),
            _ => Strings.Get("connect.failed.unavailable")
        };

    /// <returns>True when the user trusted the certificate the server presented.</returns>
    private Task<bool> ConfirmCertificateAsync(ICoordinatorSession session)
    {
        ConnectionStatus = Strings.Get("connect.status.waitingForCertificate");
        return CertificateTrustMessages.ConfirmTrustAsync(session, ShowMessageBox);
    }

    private async Task SaveCoordinatorAsync(CoordinatorSessionModel coordinatorSessionModel, SavedUserModel userModel,
        string? accountPassword)
    {
        coordinatorSessionModel.Address = _parsedAddress!;

        try
        {
            await _accountingService.SaveCoordinatorInstanceAsync(
                new SavedCoordinatorModel()
                {
                    CoordinatorId = coordinatorSessionModel.Id,
                    Address = _parsedAddress!,
                    Port = coordinatorSessionModel.Port,
                    Name = coordinatorSessionModel.Name,
                },
                new SavedUserModel()
                {
                    Id = userModel.Id,
                    UserIdentity = userModel.UserIdentity,
                    Password = this.Password,
                    AccountPassword = accountPassword,
                    SessionToken = userModel.SessionToken,
                    Username = userModel.Username,
                    CoordinatorId = coordinatorSessionModel.Id
                });

            // Keep the explicit startup target when another server is connected manually.
            if (await _behaviorService.GetAutoConnectInstance() == null)
                await _behaviorService.SetLastIdentityAsync((coordinatorSessionModel.Id, userModel.Id));
        }
        catch (Exception e)
        {
            await HandleConnectionWarning(Strings.Format("connect.saveFailed", ("error", e.Message)));
        }
    }

    private async Task HandleConnectionError(string message)
    {
        await ShowMessageBox.Handle(new MessageBoxVM(Icon.Error, Buttons.Ok, message, Strings.Get("common.error")));
    }

    private async Task HandleConnectionWarning(string message)
    {
        await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok, message, Strings.Get("common.warning")));
    }

    private async Task HandleCloseWindow()
    {
        await CloseWindow.Handle(null);
    }

    /// <summary>
    /// Moves a port typed into the address field over to the port field, once the user is done typing.
    /// </summary>
    public void NormalizeAddress()
    {
        var (address, _) = UriParsing.ParseServerAddress(ServerAddress);

        if (address?.Port is not { } port)
            return;

        Port = port == DefaultPort ? null : port.ToString();
        ServerAddress = address.Host;
    }

    private void ValidateEndpoint()
    {
        var (address, addressError) = UriParsing.ParseServerAddress(ServerAddress);
        _addressPort = address?.Port;

        ServerAddressError? portError = null;
        int port = DefaultPort;

        if (_addressPort is { } addressPort)
            port = addressPort;
        else if (!string.IsNullOrWhiteSpace(Port))
            portError = UriParsing.ValidatePort(Port, out port);

        AddressError = _isAddressEdited ? ServerAddressMessages.Get(addressError) : null;
        PortError = ServerAddressMessages.Get(portError);
        PortHint = _addressPort is { } shown ? Strings.Format("connect.port.fromAddress", ("port", shown)) : null;

        var isValid = address != null && portError == null;
        _parsedAddress = isValid ? address!.Host : null;
        _parsedPort = isValid ? port : null;
        IsAddressValid = isValid;
        IsFormValid = isValid && PasswordError == null;

        if (_identitiesEndpoint != IdentityLookupKey)
            FindIdentitiesCommand.Execute().Subscribe();
    }

    private string? SelectedCoordinatorId => SelectedSavedServer is { } server
        && UriParsing.ParseServerAddress(server.Host).Item1?.Host == _parsedAddress
        && server.Port == _parsedPort ? server.CoordinatorId : null;

    private string IdentityLookupKey => $"{SelectedCoordinatorId}:{_parsedAddress}:{_parsedPort}";

    private async Task FindIdentities()
    {
        _identitiesEndpoint = IdentityLookupKey;
        var lookup = ++_identitiesLookup;
        IsLoadingIdentities = true;

        try
        {
            // The previous server's identity must not stay selected while it is no longer in the list
            SelectedIdentity = null;
            Password = null;
            Identities.Clear();
            Identities.Add(new SavedUserModel { UserIdentity = null!, Username = Strings.Get("connect.identity.new")});

            var coordinators = IsAddressValid
                ? await _accountingService.GetSavedCoordinatorsAsync(_parsedAddress!, _parsedPort!.Value)
                : [];
            var lastIdentity = await _behaviorService.GetAutoConnectInstance();

            // A lookup for an endpoint typed in the meantime owns the list now
            if (lookup != _identitiesLookup)
                return;

            foreach (var coordinator in coordinators)
            {
                foreach (var match in coordinator.Users)
                {
                    match.CoordinatorLabel = coordinators.Count > 1
                        ? string.IsNullOrWhiteSpace(coordinator.Name) ? coordinator.CoordinatorId
                            : coordinators.Count(c => c.Name == coordinator.Name) > 1
                                ? $"{coordinator.Name}, {coordinator.CoordinatorId}" : coordinator.Name
                        : null;
                    Identities.Add(match);
                    if (lastIdentity != null && match.CoordinatorId == lastIdentity.Value.Coordinator.CoordinatorId && match.Id == lastIdentity.Value.User.Id)
                        SelectedIdentity = match;
                }
            }

            // "New*" registers another user on the server, so it is picked only when nothing is saved for it
            SelectedIdentity ??= Identities.Count > 1 ? Identities[1] : Identities[0];

            this.RaisePropertyChanged(nameof(IsIdentitiesSelectable));
        }
        finally
        {
            if (lookup == _identitiesLookup)
                IsLoadingIdentities = false;
        }
    }

    private async Task FinalizeConnectionError(ICoordinatorSession session, string message)
    {
        await HandleConnectionError(message);

        await session.Disconnect();
        session.Dispose();
    }

    public void CancelTasks()
    {
        _cancellationTokenSource.Cancel();
    }
}
