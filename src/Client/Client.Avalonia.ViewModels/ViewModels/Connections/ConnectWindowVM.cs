using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Application.Links;
using Client.Application.Ports;
using Client.Application.Security;
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

    /// <summary>The entry that registers a new account on the server.</summary>
    public SavedUserModel NewAccount { get; } = new()
    {
        Username = Strings.Get("connect.identity.new"), EntryIconKey = "PlusIcon"
    };

    /// <summary>The entry that logs in to an account this device does not know yet.</summary>
    public SavedUserModel ExistingAccount { get; } = new()
    {
        Username = Strings.Get("connect.identity.existing"), EntryIconKey = "DoorWithGreenArrowIcon"
    };

    public SavedUserModel? SelectedIdentity
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);

            // The server password saved with the account is filled in; the other entries keep what was typed
            if (IsSaved(value))
                Password = value!.Password;

            // A saved account without a device here logs in by its name, which is filled in
            if (IsSaved(value) && !value!.CanLogIn)
            {
                LoginMethod = ExistingLoginMethod.Username;
                LoginUsername = value.Username;
            }

            this.RaisePropertyChanged(nameof(IsLoginFormVisible));
            this.RaisePropertyChanged(nameof(IsRegistering));
            this.RaisePropertyChanged(nameof(IdentityError));
        }
    }

    /// <summary>
    /// Why the saved account picked has to log in again, shown in red under the list; null when it can log in.
    /// </summary>
    public string? IdentityError => IsSaved(SelectedIdentity) && !SelectedIdentity!.CanLogIn
        ? Strings.Get(SelectedIdentity.LoginLostReason switch
        {
            LoginLostReason.Revoked => "connect.identity.lost.revoked",
            LoginLostReason.NotFound => "connect.identity.lost.notFound",
            LoginLostReason.LoggedOut => "connect.identity.lost.loggedOut",
            _ => "connect.identity.lost.noDevice"
        })
        : null;

    public bool IsIdentitiesSelectable => Identities.Count > 1;

    /// <summary>The account is logged in to by one of <see cref="ExistingLoginMethod"/>, not as a saved device.</summary>
    public bool IsLoginFormVisible => ReferenceEquals(SelectedIdentity, ExistingAccount)
                                      || IsSaved(SelectedIdentity) && !SelectedIdentity!.CanLogIn;

    public bool IsRegistering => ReferenceEquals(SelectedIdentity, NewAccount);

    public ExistingLoginMethod LoginMethod
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsUsernameLogin));
            this.RaisePropertyChanged(nameof(IsRecoveryLogin));
            this.RaisePropertyChanged(nameof(IsCodeLogin));
            this.RaisePropertyChanged(nameof(IsDeviceLinkLogin));
        }
    } = ExistingLoginMethod.Username;

    public bool IsUsernameLogin
    {
        get => LoginMethod == ExistingLoginMethod.Username;
        set { if (value) LoginMethod = ExistingLoginMethod.Username; }
    }

    public bool IsRecoveryLogin
    {
        get => LoginMethod == ExistingLoginMethod.RecoveryPhrase;
        set { if (value) LoginMethod = ExistingLoginMethod.RecoveryPhrase; }
    }

    public bool IsCodeLogin
    {
        get => LoginMethod == ExistingLoginMethod.LoginCode;
        set { if (value) LoginMethod = ExistingLoginMethod.LoginCode; }
    }

    public bool IsDeviceLinkLogin
    {
        get => LoginMethod == ExistingLoginMethod.DeviceLink;
        set { if (value) LoginMethod = ExistingLoginMethod.DeviceLink; }
    }

    public string? LoginUsername
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? RecoveryPhraseText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? LoginCodeText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>A device on someone else's computer: not remembered, and logged out when the app closes.</summary>
    public bool IsTemporaryDevice
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Keep the server password, in the system keyring, so the next start does not ask.</summary>
    public bool RememberServerPassword
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>The code this device shows while a device of the account is to approve it.</summary>
    public DeviceLinkCode? DeviceLinkCode
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsDeviceLinkCodeShown));
            this.RaisePropertyChanged(nameof(DeviceLinkQrText));
        }
    }

    public bool IsDeviceLinkCodeShown => DeviceLinkCode != null;

    /// <summary>
    /// What the QR code of <see cref="DeviceLinkCode"/> holds: a link with the code and the server this device waits on,
    /// which a phone of the account scans.
    /// </summary>
    public string? DeviceLinkQrText => DeviceLinkCode is { } code && _parsedAddress != null
        ? FlowRateLinks.DeviceLink(code.Code, _parsedAddress, _parsedPort == DefaultPort ? null : _parsedPort)
        : null;

    private static bool IsSaved(SavedUserModel? identity) => identity is { CoordinatorId.Length: > 0 };

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

    /// <summary>Gives up waiting for a device of the account to approve this one.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelDeviceLinkCommand { get; }

    // The session that waits for a device link, to give the wait up
    private ICoordinatorSession? _linkingSession;
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

        CancelDeviceLinkCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_linkingSession is { } linking)
            {
                _deviceLinkCancelled = true;
                await linking.Disconnect();
            }
        });

        FindIdentitiesCommand = ReactiveCommand.CreateFromTask(FindIdentities);

        Task.Run(async () =>
        {
            var savedServers = await _accountingService.GetAllSavedCoordinatorsAsync();

            Dispatcher.UIThread.Post(() =>
            {
                // The account the window was opened for, else the server connected to last. Read on the UI thread,
                // after whoever made the window had the chance to call PreferAccount
                var lastServer = savedServers.FirstOrDefault(server => server.CoordinatorId == _preferredAccount?.CoordinatorId)
                                 ?? savedServers.MaxBy(server => server.SaveOrder);

                foreach (var server in savedServers)
                    SavedServers.Add(new SavedServerItem(server.CoordinatorId, server.Name, UriParsing.FormatHost(server.Address), server.Port));

                if (!_isAddressEdited && lastServer != null)
                    SelectedSavedServer = SavedServers.FirstOrDefault(s => s.CoordinatorId == lastServer.CoordinatorId);
            });
        });

    }

    private bool _deviceLinkCancelled;

    // The saved account the window is opened for, when the one connected to at start has to log in again
    private (string CoordinatorId, int UserId)? _preferredAccount;

    /// <summary>
    /// Opens the window on a saved account rather than on the server connected to last. Called right after the window
    /// is made, before its saved servers are listed.
    /// </summary>
    public void PreferAccount(string coordinatorId, int userId) => _preferredAccount = (coordinatorId, userId);

    // The server a link opened the window on, with the certificate it vouches for
    private (string Host, int Port, string Fingerprint)? _linkCertificate;

    // The one-time code of a login link, picked again whenever the accounts of the server are listed
    private string? _linkLoginCode;

    /// <summary>
    /// Fills the form from a link or a scanned code: the server, and the login code of a login link. Nothing connects
    /// until the user does, since any page can open a link.
    /// </summary>
    public void OpenLink(FlowRateLink link)
    {
        string host;
        int? port;
        string? fingerprint;
        string? code = null;
        switch (link)
        {
            case ConnectLink connect:
                (host, port, fingerprint) = (connect.Host, connect.Port, connect.Fingerprint);
                break;
            case LoginLink login:
                (host, port, fingerprint, code) = (login.Host, login.Port, login.Fingerprint, login.Code);
                break;
            default:
                return;
        }

        _linkLoginCode = code;
        SelectedSavedServer = null;
        Port = port is { } given && given != DefaultPort ? given.ToString() : null;
        ServerAddress = host;

        _linkCertificate = fingerprint != null && _parsedAddress != null && _parsedPort is { } parsedPort
            ? (_parsedAddress, parsedPort, fingerprint)
            : null;
        if (code != null && !IsLoadingIdentities)
            PickLinkLogin();
    }

    /// <summary>
    /// Fills the form from the text of a scanned QR code.
    /// </summary>
    /// <returns>False when the code holds no server to connect to.</returns>
    public bool OpenScanned(string? text)
    {
        if (FlowRateLinks.Parse(text) is not { } link || link is not (ConnectLink or LoginLink))
            return false;

        OpenLink(link);
        return true;
    }

    /// <summary>
    /// Reads a QR code with the scanner of the phone and fills the form from it, telling the user what went wrong.
    /// </summary>
    public async Task ScanAsync(Func<Task<string?>> scan)
    {
        string? text;
        try
        {
            text = await scan();
        }
        catch (Exception exception)
        {
            await HandleConnectionError(Strings.Format("link.scan.unavailable", ("error", exception.Message)));
            return;
        }

        if (text == null || OpenScanned(text))
            return;

        await HandleConnectionError(Strings.Get(FlowRateLinks.Parse(text) is DeviceLinkApproval
            ? "link.scan.deviceLinkHere"
            : "link.scan.notRecognized"));
    }

    private void PickLinkLogin()
    {
        if (_linkLoginCode is not { } code)
            return;

        SelectedIdentity = ExistingAccount;
        LoginMethod = ExistingLoginMethod.LoginCode;
        LoginCodeText = code;
    }

    // The certificate a link vouched for is trusted without asking, on the server of the link only and never in place
    // of one trusted before: a changed certificate is asked about as always
    private bool IsVouchedByLink(UntrustedCertificate untrusted) =>
        !untrusted.IsChanged
        && _linkCertificate is var (host, port, fingerprint)
        && host == _parsedAddress && port == _parsedPort
        && FlowRateLinks.IsSameFingerprint(fingerprint, untrusted.Certificate.Fingerprint);

    private async Task ConnectAsync()
    {
        if (!IsFormValid)
            return;
        if (CreateLoginPlan() is not { } plan)
            return;
        var identity = SelectedIdentity;

        _deviceLinkCancelled = false;
        var session = _coordinatorFactory.CreateCoordinatorSession();
        LoginPromptVM.Attach(session, AskLogin);

        // Only the reason of a rejected login is needed, the main window takes over the session after
        RequestFailedException? rejection = null;
        void OnRejected(RequestFailedException exception) => rejection = exception;
        void OnLinkCode(DeviceLinkCode code) => Dispatcher.UIThread.Post(() =>
        {
            if (!_deviceLinkCancelled && ReferenceEquals(_linkingSession, session))
                DeviceLinkCode = code;
        });
        session.RequestRejected += OnRejected;
        session.DeviceLinkCodeReceived += OnLinkCode;
        if (plan is DeviceLinkLoginPlan)
            _linkingSession = session;

        try
        {
            if (!await ConnectWithCertificateAsync(session, plan) || _deviceLinkCancelled)
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

            // Ended while this device was away: the account is marked right here and asks for a login
            if (session.ConnectionStatus == CoordinatorState.DeviceEnded && IsSaved(identity))
                await MarkDeviceEndedAsync(identity!);

            if (session.ConnectionStatus != CoordinatorState.Connected)
            {
                var message = DescribeConnectionFailure(session.ConnectionStatus, rejection, session.BanReason);
                session.Dispose();
                await HandleConnectionError(message);
                return;
            }

            // Set by the successful login above. A saved account is stored again as well, so it becomes the last one
            // and is offered first next time. A temporary device leaves nothing behind
            if (!session.IsTemporaryDevice)
                await SaveCoordinatorAsync(session.CoordinatorModel!, session.GetCurrentUserCredentials(),
                    session.TakeNewRecoveryPhrase());

            await CloseWindow.Handle(session);
        }
        catch (OperationCanceledException)
        {
            await session.Disconnect();
            session.Dispose();
        }
        catch (Exception) when (_deviceLinkCancelled)
        {
            session.Dispose();
        }
        catch (RequestFailedException ex)
        {
            await FinalizeConnectionError(session, ex.Code == Shared.Enums.ErrorCode.AuthFailed
                ? Strings.Get("connect.failed.authFailed")
                : RequestRejectionMessages.GetForConnection(ex.Code));
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
            session.DeviceLinkCodeReceived -= OnLinkCode;
            _linkingSession = null;
            DeviceLinkCode = null;
            ConnectionStatus = Strings.Get("connect.status.waitingForAction");
        }
    }

    /// <summary>Forgets the device of a saved account the server no longer knows and shows the account as such.</summary>
    private async Task MarkDeviceEndedAsync(SavedUserModel identity)
    {
        try
        {
            await _accountingService.ForgetDeviceAsync(identity.CoordinatorId, identity.Id, LoginLostReason.NotFound);
        }
        catch (Exception)
        {
            // The list still shows the account as ended; the database is cleared by the next login that fails so
        }

        var index = Identities.IndexOf(identity);
        if (index < 0)
            return;
        var ended = identity.WithoutDevice(LoginLostReason.NotFound);
        Identities[index] = ended;
        if (ReferenceEquals(SelectedIdentity, identity) || SelectedIdentity == null)
            SelectedIdentity = ended;
    }

    /// <summary>How the selected entry logs in; null when what is typed is not enough, which the user is told.</summary>
    private LoginPlan? CreateLoginPlan()
    {
        var identity = SelectedIdentity;
        if (identity == null || ReferenceEquals(identity, NewAccount))
            return new RegisterLoginPlan();
        if (!IsLoginFormVisible)
            return LoginPlan.For(identity);

        return LoginMethod switch
        {
            ExistingLoginMethod.Username when !string.IsNullOrWhiteSpace(LoginUsername) =>
                new UsernameLoginPlan(LoginUsername, IsTemporaryDevice),
            ExistingLoginMethod.RecoveryPhrase when RecoveryPhrase.Normalize(RecoveryPhraseText) is { } phrase =>
                new RecoveryLoginPlan(phrase, IsTemporaryDevice),
            ExistingLoginMethod.LoginCode when !string.IsNullOrWhiteSpace(LoginCodeText) =>
                new LoginCodeLoginPlan(LoginCodeText, IsTemporaryDevice),
            ExistingLoginMethod.DeviceLink => new DeviceLinkLoginPlan(IsTemporaryDevice),
            ExistingLoginMethod.RecoveryPhrase => Refuse("connect.login.phraseInvalid"),
            _ => Refuse("connect.login.fieldEmpty")
        };

        LoginPlan? Refuse(string message)
        {
            _ = HandleConnectionError(Strings.Get(message));
            return null;
        }
    }

    private async Task<bool> ConnectWithCertificateAsync(ICoordinatorSession session, LoginPlan plan)
    {
        ConnectionStatus = Strings.Get(plan is DeviceLinkLoginPlan
            ? "connect.status.waitingForApproval" : "main.status.waitingForServer");

        var expectedCoordinatorId = SelectedIdentity?.CoordinatorId is { Length: > 0 } id ? id : null;
        await session.Connect(_parsedAddress!, _parsedPort!.Value, Password, plan, expectedCoordinatorId);

        _cancellationTokenSource.Token.ThrowIfCancellationRequested();

        if (session.ConnectionStatus == CoordinatorState.CertificateNotTrusted)
        {
            if (session.UntrustedCertificate is { } untrusted && IsVouchedByLink(untrusted))
                await session.TrustCertificateAsync(untrusted);
            else if (!await ConfirmCertificateAsync(session))
                return false;

            ConnectionStatus = Strings.Get("main.status.waitingForServer");
            await session.Connect(_parsedAddress!, _parsedPort!.Value, Password, plan, expectedCoordinatorId);

            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
        }

        return true;
    }

    private static string DescribeConnectionFailure(CoordinatorState state, RequestFailedException? rejection, string? banReason) =>
        state switch
        {
            CoordinatorState.OnlineLimitReached => Strings.Get("connect.failed.onlineLimitReached"),
            CoordinatorState.UserLimitReached => Strings.Get("connect.failed.userLimitReached"),
            CoordinatorState.AuthenticationFailed => Strings.Get("connect.failed.authFailed"),
            CoordinatorState.Denied => Strings.Get("connect.failed.banned") + (string.IsNullOrWhiteSpace(banReason) ? "" : "\n" + banReason),
            CoordinatorState.PasskeyRequired => Strings.Get("connect.failed.passkeyRequired"),
            CoordinatorState.DeviceEnded => Strings.Get("connect.failed.deviceEnded"),
            CoordinatorState.LoginFactorsMissing => Strings.Get("connect.failed.loginFactorsMissing"),
            CoordinatorState.DeviceLinkDeclined => Strings.Get("connect.failed.deviceLinkDeclined"),
            CoordinatorState.LoginRefused => Strings.Get("connect.failed.loginRefused"),
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
        string? newRecoveryPhrase)
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
                    Password = RememberServerPassword ? Password : null,
                    Username = userModel.Username,
                    CoordinatorId = coordinatorSessionModel.Id,
                    DeviceId = userModel.DeviceId,
                    DeviceKey = userModel.DeviceKey,
                    BackupKey = userModel.BackupKey,
                    // A phrase made just now waits in the keyring until the user saves it; an older one stays as it is
                    RecoveryPhrase = newRecoveryPhrase,
                    IsRecoveryPhraseSaved = newRecoveryPhrase == null
                        && Identities.FirstOrDefault(saved => IsSaved(saved) && saved.Id == userModel.Id
                            && saved.CoordinatorId == coordinatorSessionModel.Id) is { IsRecoveryPhraseSaved: true }
                });

            // The startup connection follows the last one unless the user picked another in the settings
            await _behaviorService.RecordConnectionAsync(coordinatorSessionModel.Id, userModel.Id);
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
            Identities.Add(NewAccount);
            Identities.Add(ExistingAccount);

            var coordinators = IsAddressValid
                ? await _accountingService.GetSavedCoordinatorsAsync(_parsedAddress!, _parsedPort!.Value)
                : [];
            // The account the window is opened for, else the one that last connected to the server used last
            var lastIdentity = _preferredAccount
                ?? (coordinators.MaxBy(coordinator => coordinator.SaveOrder) is { LastUserId: { } lastUserId } latest
                    ? (latest.CoordinatorId, lastUserId)
                    : ((string CoordinatorId, int UserId)?)null);

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
                    if (lastIdentity != null && match.CoordinatorId == lastIdentity.Value.CoordinatorId && match.Id == lastIdentity.Value.UserId)
                        SelectedIdentity = match;
                }
            }

            // "New" registers another user on the server, so it is picked only when nothing is saved for it
            SelectedIdentity ??= Identities.Count > 2 ? Identities[2] : Identities[0];
            PickLinkLogin();

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

/// <summary>How an account this device does not know yet is logged in to.</summary>
public enum ExistingLoginMethod
{
    /// <summary>The username, then the password and/or one-time code.</summary>
    Username,

    /// <summary>The 24 words of the recovery phrase.</summary>
    RecoveryPhrase,

    /// <summary>A one-time code from the administrator of the server.</summary>
    LoginCode,

    /// <summary>Approved from another device of the account, by the code this one shows.</summary>
    DeviceLink
}
