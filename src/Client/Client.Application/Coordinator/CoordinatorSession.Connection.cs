using System.Threading.Channels;
using Shared.Enums.Auth;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Client.Application.Ports;

namespace Client.Application.Coordinator;

// The connection is owned by one loop. Everything that concerns it - what the user asked for, the
// application leaving the screen, the network, a lost socket, an attempt that ended - comes as a
// message and is handled one at a time, so the state below is never touched by two threads.
public partial class CoordinatorSession
{
    private enum ConnectionPhase
    {
        // Never connected, or ended by the user, the server or an error retrying cannot fix
        Idle,
        Establishing,
        Online,
        WaitingForRetry,
        Suspended
    }

    /// <param name="Completion">Completed once the message is handled, for a caller that waits for it.</param>
    private abstract record ConnectionMessage(TaskCompletionSource? Completion = null);

    private sealed record ConnectRequested(string Address, int Port, string? UserIdentity, string? Password,
        TaskCompletionSource Completion) : ConnectionMessage(Completion);

    private sealed record DisconnectRequested(TaskCompletionSource Completion) : ConnectionMessage(Completion);

    private sealed record VisibilityChanged(bool IsInFront, TaskCompletionSource Completion) : ConnectionMessage(Completion);

    private sealed record SuspendDelayElapsed(int TimerId) : ConnectionMessage;

    private sealed record RetryDelayElapsed(int TimerId) : ConnectionMessage;

    private sealed record AttemptCompleted(int AttemptId, CoordinatorState State, Exception? Exception) : ConnectionMessage;

    private sealed record ConnectionLost(string Reason, int Epoch) : ConnectionMessage;

    private sealed record NetworkChanged(bool IsAvailable) : ConnectionMessage;

    private sealed record CheckCompleted(int Epoch, bool IsAlive) : ConnectionMessage;

    public event Action<int, TimeSpan>? ReconnectScheduled;

    public UntrustedCertificate? UntrustedCertificate { get; private set; }

    public CoordinatorMismatch? CoordinatorMismatch { get; private set; }

    /// <summary>
    /// How long the connection is kept after the application left the screen. Within it a short
    /// trip to another application, a file picker for one, costs no reconnect.
    /// </summary>
    internal TimeSpan SuspendDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long an answer is waited for when the connection is checked out of turn.</summary>
    internal TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    private readonly Channel<ConnectionMessage> _connectionMessages =
        Channel.CreateUnbounded<ConnectionMessage>(new UnboundedChannelOptions { SingleReader = true });

    // The address the user connected to. The one the coordinator reports about itself may be
    // unreachable from here, for example behind NAT.
    private volatile string? _address;
    private int _port;

    public string? ConnectedAddress => _address is { } address ? $"{address}:{_port}" : null;

    // Everything below belongs to the loop
    private ConnectionPhase _phase = ConnectionPhase.Idle;
    private bool _isInFront = true;
    private bool _isNetworkAvailable = true;
    private DateTime _leftFrontAt;

    // Counts the connections, so that the loss of one that is already replaced is told apart.
    // Read by the threads that report a loss.
    private int _epoch;

    // The first connect, which is not retried and whose outcome its caller waits for
    private ConnectRequested? _pendingConnect;

    // Those who wait for the running restore to end, either way
    private readonly List<TaskCompletionSource> _restoreWaiters = [];

    private int _attempt;
    private int _attemptId;
    private CancellationTokenSource? _attemptCancellation;
    private Task _attemptTask = Task.CompletedTask;
    private bool _isLostDuringAttempt;

    private int _retryTimerId;
    private CancellationTokenSource? _retryTimer;
    private int _suspendTimerId;
    private CancellationTokenSource? _suspendTimer;

    private int? _checkedEpoch;

    // Canceled when the session is disposed; never disposed itself, the loop still reads it afterwards
    private readonly CancellationTokenSource _lifetime = new();

    public Task Connect(string address, int port, string? userIdentity, string? password) =>
        PostAndWait(completion => new ConnectRequested(address, port, userIdentity, password, completion));

    public Task Disconnect() => PostAndWait(completion => new DisconnectRequested(completion));

    public Task Suspend() => PostAndWait(completion => new VisibilityChanged(false, completion));

    public Task Resume() => PostAndWait(completion => new VisibilityChanged(true, completion));

    public async Task TrustCertificateAsync(UntrustedCertificate certificate)
    {
        await _trustedCertificates.SaveAsync(certificate.Certificate);
        LogCertificateTrusted(LogName, certificate.Certificate.Fingerprint, certificate.Certificate.Host,
            certificate.Certificate.Port);

        if (UntrustedCertificate == certificate)
            UntrustedCertificate = null;
    }

    private Task PostAndWait(Func<TaskCompletionSource, ConnectionMessage> createMessage)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Refused only once the session is disposed, when there is nothing left to wait for
        if (!_connectionMessages.Writer.TryWrite(createMessage(completion)))
            completion.TrySetResult();

        return completion.Task;
    }

    private void Post(ConnectionMessage message) => _connectionMessages.Writer.TryWrite(message);

    // Raised on a transport thread
    private void OnConnectionLost(string reason) => Post(new ConnectionLost(reason, Volatile.Read(ref _epoch)));

    // Raised on a thread of the monitor
    private void OnNetworkChanged(bool isAvailable) => Post(new NetworkChanged(isAvailable));

    private async Task RunConnectionLoopAsync()
    {
        await foreach (var message in _connectionMessages.Reader.ReadAllAsync())
        {
            // Posted before the session was disposed: there is nothing left to do it with
            if (_lifetime.IsCancellationRequested)
            {
                message.Completion?.TrySetResult();
                continue;
            }

            try
            {
                await HandleAsync(message);
            }
            catch (Exception exception)
            {
                // Nobody awaits the loop, so a failure would go unnoticed and leave the state as it was
                LogConnectionHandlingFailed(exception, LogName, message.GetType().Name);
                await FailAsync(exception);
                message.Completion?.TrySetException(exception);
            }
        }

        CompletePendingConnect();
        CompleteRestore();
    }

    private Task HandleAsync(ConnectionMessage message) => message switch
    {
        ConnectRequested connect => HandleConnectAsync(connect),
        DisconnectRequested disconnect => HandleDisconnectAsync(disconnect),
        VisibilityChanged { IsInFront: false } left => HandleLeftFront(left),
        VisibilityChanged returned => HandleReturnedToFrontAsync(returned),
        SuspendDelayElapsed elapsed => HandleSuspendDelayElapsedAsync(elapsed),
        RetryDelayElapsed elapsed => HandleRetryDelayElapsed(elapsed),
        AttemptCompleted completed => HandleAttemptCompletedAsync(completed),
        ConnectionLost lost => HandleConnectionLostAsync(lost),
        NetworkChanged changed => HandleNetworkChanged(changed),
        CheckCompleted completed => HandleCheckCompletedAsync(completed),
        _ => Task.CompletedTask
    };

    private async Task HandleConnectAsync(ConnectRequested connect)
    {
        await CloseConnectionAsync();
        CompletePendingConnect();

        _isInFront = true;
        _pendingConnect = connect;
        ConnectionStatus = CoordinatorState.Connecting;
        LogConnectStarted(LogName, connect.Address, connect.Port);

        StartAttempt(connect.Address, connect.Port, connect.UserIdentity, connect.Password);
    }

    private async Task HandleDisconnectAsync(DisconnectRequested disconnect)
    {
        await CloseConnectionAsync();

        _phase = ConnectionPhase.Idle;
        ConnectionStatus = CoordinatorState.Disconnected;
        LogDisconnected(LogName, CoordinatorModel?.Id);

        CompletePendingConnect();
        CompleteRestore();
        disconnect.Completion?.TrySetResult();
    }

    private Task HandleLeftFront(VisibilityChanged left)
    {
        if (_isInFront)
        {
            _isInFront = false;
            _leftFrontAt = DateTime.UtcNow;

            if (_phase != ConnectionPhase.Idle)
                StartTimer(ref _suspendTimer, SuspendDelay, new SuspendDelayElapsed(++_suspendTimerId));
        }

        left.Completion?.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task HandleSuspendDelayElapsedAsync(SuspendDelayElapsed elapsed)
    {
        if (elapsed.TimerId == _suspendTimerId && !_isInFront)
            await SuspendAsync();
    }

    private async Task SuspendAsync()
    {
        if (_phase is ConnectionPhase.Idle or ConnectionPhase.Suspended || _pendingConnect != null)
            return;

        await CloseConnectionAsync();

        _phase = ConnectionPhase.Suspended;
        ConnectionStatus = CoordinatorState.Suspended;
        LogSuspended(LogName);
        CompleteRestore();
    }

    private async Task HandleReturnedToFrontAsync(VisibilityChanged returned)
    {
        if (_isInFront)
        {
            returned.Completion?.TrySetResult();
            return;
        }

        _isInFront = true;
        StopTimer(ref _suspendTimer, ref _suspendTimerId);

        // A process the system froze off the screen misses its timer, and its connection went
        // unanswered for as long: the server has given it up by now
        if (_phase == ConnectionPhase.Online && DateTime.UtcNow - _leftFrontAt >= SuspendDelay)
            await SuspendAsync();

        switch (_phase)
        {
            case ConnectionPhase.Suspended:
                // Closed from here, not lost: restored at once and without the look of a failure
                BeginRestore(atOnce: true, CoordinatorState.Connecting);
                break;
            case ConnectionPhase.WaitingForRetry:
                RetryAtOnce();
                break;
            case ConnectionPhase.Online:
                StartCheck();
                break;
        }

        // The caller waits for the restore it started to end, either way
        if (_phase is ConnectionPhase.Establishing or ConnectionPhase.WaitingForRetry && _pendingConnect == null
            && returned.Completion != null)
            _restoreWaiters.Add(returned.Completion);
        else
            returned.Completion?.TrySetResult();
    }

    private async Task HandleConnectionLostAsync(ConnectionLost lost)
    {
        if (lost.Epoch != _epoch)
            return;

        // The attempt may still end well, having got its answers before the socket broke
        if (_phase == ConnectionPhase.Establishing)
        {
            _isLostDuringAttempt = true;
            return;
        }

        if (_phase != ConnectionPhase.Online)
            return;

        LogConnectionLost(LogName, lost.Reason);
        await RestoreLostConnectionAsync();
    }

    private async Task RestoreLostConnectionAsync()
    {
        // The socket of a server that stopped answering is still open
        await CloseConnectionAsync();

        // Nobody is looking: the device is notified instead, and the connection comes back with the user
        if (!_isInFront)
        {
            StopTimer(ref _suspendTimer, ref _suspendTimerId);
            _phase = ConnectionPhase.Suspended;
            ConnectionStatus = CoordinatorState.Suspended;
            LogSuspended(LogName);
            return;
        }

        BeginRestore(atOnce: false, CoordinatorState.Reconnecting);
    }

    /// <param name="atOnce">The first attempt does not wait: the connection was closed from here, not lost.</param>
    private void BeginRestore(bool atOnce, CoordinatorState state)
    {
        _attempt = 1;
        ConnectionStatus = state;

        // Anything could have changed while we were away
        _usersDirectory.Invalidate();

        ScheduleAttempt(atOnce ? TimeSpan.Zero : GetRetryDelay());
    }

    // Without a network the attempts go on, but rarely: the monitor is a hint and may be wrong
    private TimeSpan GetRetryDelay() =>
        _isNetworkAvailable ? ReconnectBackoff.GetSpreadDelay(_attempt) : ReconnectBackoff.LongestDelay;

    private void ScheduleAttempt(TimeSpan delay)
    {
        _phase = ConnectionPhase.WaitingForRetry;
        ReconnectScheduled?.Invoke(_attempt, delay);

        if (delay > TimeSpan.Zero)
            StartTimer(ref _retryTimer, delay, new RetryDelayElapsed(++_retryTimerId));
        else
            StartRestoreAttempt();
    }

    private void RetryAtOnce()
    {
        StopTimer(ref _retryTimer, ref _retryTimerId);
        _attempt = 1;
        ScheduleAttempt(TimeSpan.Zero);
    }

    private Task HandleRetryDelayElapsed(RetryDelayElapsed elapsed)
    {
        if (elapsed.TimerId == _retryTimerId && _phase == ConnectionPhase.WaitingForRetry)
            StartRestoreAttempt();

        return Task.CompletedTask;
    }

    private void StartRestoreAttempt()
    {
        LogReconnectAttemptStarted(LogName, _attempt);
        StartAttempt(_address!, _port, GetCurrentUserCredentials().UserIdentity, _password);
    }

    private void StartAttempt(string address, int port, string? userIdentity, string? password)
    {
        _phase = ConnectionPhase.Establishing;
        _isLostDuringAttempt = false;
        Interlocked.Increment(ref _epoch);

        var attemptId = ++_attemptId;
        _attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var cancellationToken = _attemptCancellation.Token;

        // Off the caller's thread and off the loop: the first request of a run builds the
        // serializer metadata of every message type while it is written
        _attemptTask = Task.Run(async () =>
        {
            try
            {
                var state = await EstablishAsync(address, port, userIdentity, password, cancellationToken);
                Post(new AttemptCompleted(attemptId, state, null));
            }
            catch (Exception exception)
            {
                Post(new AttemptCompleted(attemptId, CoordinatorState.Failed, exception));
            }
        }, CancellationToken.None);
    }

    private async Task HandleAttemptCompletedAsync(AttemptCompleted completed)
    {
        if (completed.AttemptId != _attemptId || _phase != ConnectionPhase.Establishing)
            return;

        _attemptCancellation?.Dispose();
        _attemptCancellation = null;

        var state = completed.State;

        if (state == CoordinatorState.Connected && _isLostDuringAttempt)
        {
            LogConnectionLost(LogName, "lost while logging in");
            await CloseConnectionAsync();
            state = CoordinatorState.Failed;
        }

        if (_pendingConnect is { } connect)
        {
            _pendingConnect = null;

            if (state == CoordinatorState.Connected)
            {
                _port = connect.Port;
                _address = connect.Address;
                _password = connect.Password;
                _phase = ConnectionPhase.Online;
            }
            else
                _phase = ConnectionPhase.Idle;

            ConnectionStatus = state;

            // Left while it was connecting: the pause before closing starts over from here
            if (_phase == ConnectionPhase.Online && !_isInFront)
                StartTimer(ref _suspendTimer, SuspendDelay, new SuspendDelayElapsed(++_suspendTimerId));

            if (completed.Exception != null)
                connect.Completion?.TrySetException(completed.Exception);
            else
                connect.Completion?.TrySetResult();

            return;
        }

        if (state == CoordinatorState.Connected)
        {
            LogReconnected(LogName, _attempt);
            _phase = ConnectionPhase.Online;
            ConnectionStatus = CoordinatorState.Connected;
            CompleteRestore();
            return;
        }

        // Retrying cannot fix credentials the server rejected, a ban, a server only the user can trust
        // or a protocol only an update of one side can match
        if (state is CoordinatorState.AuthenticationFailed or CoordinatorState.Denied
            or CoordinatorState.CertificateNotTrusted or CoordinatorState.CoordinatorMismatch
            or CoordinatorState.ClientOutdated or CoordinatorState.ServerOutdated)
        {
            _phase = ConnectionPhase.Idle;
            ConnectionStatus = state;
            CompleteRestore();
            return;
        }

        if (completed.Exception != null)
            LogReconnectAttemptFailed(LogName, _attempt, completed.Exception.Message);

        _attempt++;
        ConnectionStatus = CoordinatorState.Reconnecting;
        ScheduleAttempt(GetRetryDelay());
    }

    private Task HandleNetworkChanged(NetworkChanged changed)
    {
        _isNetworkAvailable = changed.IsAvailable;
        LogNetworkChanged(LogName, changed.IsAvailable);

        switch (_phase)
        {
            // The socket may be bound to the network that is gone, which the heartbeat is slow to notice
            case ConnectionPhase.Online:
                StartCheck();
                break;
            case ConnectionPhase.WaitingForRetry when changed.IsAvailable:
                RetryAtOnce();
                break;
        }

        return Task.CompletedTask;
    }

    private void StartCheck()
    {
        var epoch = _epoch;
        if (_checkedEpoch == epoch)
            return;

        _checkedEpoch = epoch;
        var timeout = CheckTimeout;

        _ = Task.Run(async () =>
        {
            bool isAlive;

            try
            {
                isAlive = await _gateway.CheckConnectionAsync(timeout);
            }
            catch (Exception)
            {
                isAlive = false;
            }

            Post(new CheckCompleted(epoch, isAlive));
        });
    }

    private async Task HandleCheckCompletedAsync(CheckCompleted completed)
    {
        if (_checkedEpoch == completed.Epoch)
            _checkedEpoch = null;

        if (completed.IsAlive || completed.Epoch != _epoch || _phase != ConnectionPhase.Online)
            return;

        LogConnectionLost(LogName, "the server did not answer after the network changed");
        await RestoreLostConnectionAsync();
    }

    /// <summary>
    /// Stops whatever is under way and closes the transport. The phase is left for the caller to set.
    /// </summary>
    private async Task CloseConnectionAsync()
    {
        StopTimer(ref _retryTimer, ref _retryTimerId);
        StopTimer(ref _suspendTimer, ref _suspendTimerId);

        // The attempt is waited for: it is the only other user of the gateway, and two of them
        // would close each other's sockets
        _attemptId++;
        if (_attemptCancellation is { } attemptCancellation)
        {
            _attemptCancellation = null;
            await attemptCancellation.CancelAsync();
            await _attemptTask;
            attemptCancellation.Dispose();
            LogReconnectCanceled(LogName);
        }

        // The loss of what is closed here is nobody's news
        Interlocked.Increment(ref _epoch);
        await _gateway.DisconnectWebSocketAsync();
    }

    private async Task FailAsync(Exception exception)
    {
        try
        {
            await CloseConnectionAsync();
        }
        catch (Exception closeException)
        {
            LogConnectionHandlingFailed(closeException, LogName, nameof(CloseConnectionAsync));
        }

        _phase = ConnectionPhase.Idle;
        ConnectionStatus = CoordinatorState.Failed;

        if (_pendingConnect is { } connect)
        {
            _pendingConnect = null;
            connect.Completion?.TrySetException(exception);
        }

        CompleteRestore();
    }

    private void CompletePendingConnect()
    {
        _pendingConnect?.Completion?.TrySetResult();
        _pendingConnect = null;
    }

    private void CompleteRestore()
    {
        foreach (var waiter in _restoreWaiters)
            waiter.TrySetResult();

        _restoreWaiters.Clear();
    }

    private void StartTimer(ref CancellationTokenSource? timer, TimeSpan delay, ConnectionMessage message)
    {
        timer?.Cancel();
        timer?.Dispose();
        timer = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        var cancellationToken = timer.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
                Post(message);
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);
    }

    // The number moves on as well: a timer that fired just before it was stopped has already posted
    private static void StopTimer(ref CancellationTokenSource? timer, ref int timerId)
    {
        timerId++;
        timer?.Cancel();
        timer?.Dispose();
        timer = null;
    }

    /// <summary>
    /// Opens the transport, authenticates and loads the coordinator info. Leaves the transport
    /// closed unless it returns <see cref="CoordinatorState.Connected"/>.
    /// </summary>
    private async Task<CoordinatorState> EstablishAsync(string address, int port, string? userIdentity,
        string? password, CancellationToken cancellationToken)
    {
        var trusted = await _trustedCertificates.GetAsync(address, port);
        var connectResult = await _gateway.ConnectWebSocketAsync(address, port, trusted?.Fingerprint, cancellationToken);

        if (!connectResult.IsSuccess)
        {
            if (connectResult.RejectedCertificate is { } rejected)
            {
                UntrustedCertificate = new UntrustedCertificate(rejected, trusted?.Fingerprint);
                if (trusted == null)
                    LogCertificateNotTrusted(LogName, rejected.Fingerprint, address, port);
                else
                    LogCertificateChanged(LogName, address, port, trusted.Fingerprint, rejected.Fingerprint);
                return CoordinatorState.CertificateNotTrusted;
            }

            if (connectResult.ProtocolMismatch is { } mismatch)
                return mismatch.IsClientOutdated ? CoordinatorState.ClientOutdated : CoordinatorState.ServerOutdated;

            LogConnectFailed(LogName, connectResult.ErrorMessage);
            return CoordinatorState.Failed;
        }

        UntrustedCertificate = null;
        CoordinatorMismatch = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            LogAuthStarted(LogName);
            var response = await _gateway.AuthenticateAsync(password, userIdentity).WaitAsync(cancellationToken);

            if (response is { Result: AuthResult.Banned })
            {
                LogAuthFailed(LogName);
                await _gateway.DisconnectWebSocketAsync();
                return CoordinatorState.Denied;
            }

            if (response is not { Result: AuthResult.Success, User: not null })
            {
                LogAuthFailed(LogName);
                // The server keeps an unauthenticated socket open until it is closed from here
                await _gateway.DisconnectWebSocketAsync();
                return CoordinatorState.AuthenticationFailed;
            }

            var coordinatorInfo = await _gateway.GetCoordinatorInfo().WaitAsync(cancellationToken)
                                  ?? throw new ApplicationException("Failed to update coordinator info");

            if (trusted != null && !await CheckCoordinatorBindingAsync(trusted, coordinatorInfo))
            {
                await _gateway.DisconnectWebSocketAsync();
                return CoordinatorState.CoordinatorMismatch;
            }

            await LoadRolesAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            CoordinatorModel = coordinatorInfo;
            // Remembered like the users met later, so the account shows its avatar before the next login
            _usersDirectory.Upsert(response.User);
            SetCurrentUser(response.User, response.UserIdentity ?? string.Empty);
            LogConnected(LogName, CoordinatorModel.Id);

            return CoordinatorState.Connected;
        }
        catch (RequestRejectedException exception)
        {
            await _gateway.DisconnectWebSocketAsync();
            return OnRequestRejected(exception, CoordinatorState.Rejected);
        }
        catch (TimeoutException)
        {
            LogAuthTimedOut(LogName);
            await _gateway.DisconnectWebSocketAsync();
            return CoordinatorState.Failed;
        }
        catch (OperationCanceledException)
        {
            LogAuthCanceled(LogName);
            await _gateway.DisconnectWebSocketAsync();
            throw;
        }
        catch (Exception)
        {
            await _gateway.DisconnectWebSocketAsync();
            throw;
        }
    }

    /// <summary>
    /// Binds the coordinator on the first login behind a trusted certificate, later makes sure the
    /// address still leads to it.
    /// </summary>
    /// <returns>False when another coordinator answered.</returns>
    private async Task<bool> CheckCoordinatorBindingAsync(ServerCertificateModel trusted, CoordinatorSessionModel coordinator)
    {
        if (trusted.CoordinatorId == null)
        {
            await _trustedCertificates.BindCoordinatorAsync(trusted.Host, trusted.Port, coordinator.Id);
            return true;
        }

        if (trusted.CoordinatorId == coordinator.Id)
            return true;

        CoordinatorMismatch = new CoordinatorMismatch(trusted.Host, trusted.Port, trusted.CoordinatorId, coordinator.Id,
            coordinator.Name);
        LogCoordinatorMismatch(LogName, trusted.Host, trusted.Port, trusted.CoordinatorId, coordinator.Id);
        return false;
    }
}
