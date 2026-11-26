using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Ports;
using Client.Application.Settings;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Enums.User;

namespace Client.Services;

/// <summary>
/// Tells the server of the coordinator connected now how long the computer has gone without input, so others see
/// the user idle, then sleeping, while they picked online. Works on the UI thread.
/// </summary>
public sealed class AutoIdleService : IDisposable
{
    // Often enough that coming back shows online within a few seconds; reading the idle time costs next to nothing
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly IUserIdleProvider _idle;
    private readonly IBehaviorService _behavior;
    private readonly ILogger<AutoIdleService> _logger;
    private readonly DispatcherTimer _timer;
    private BehaviorSettings _settings = new();
    private ICoordinatorSession? _session;
    private Func<bool> _isInCall = () => false;

    // What the server holds for the connection; a new connection starts active there
    private IdleLevel _sent = IdleLevel.Active;
    private bool _sending;

    // The server of the coordinator does not know idle levels yet
    private bool _unsupported;

    public AutoIdleService(IUserIdleProvider idle, IBehaviorService behavior, ILogger<AutoIdleService> logger)
    {
        _idle = idle;
        _behavior = behavior;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => _ = CheckAsync();
        behavior.SettingsChanged += OnSettingsChanged;
        _ = LoadSettingsAsync();
    }

    /// <summary>Follows the coordinator, until <see cref="Detach"/> or another one is attached.</summary>
    /// <param name="isInCall">Whether the user is in a call now, asked on every check.</param>
    public void Attach(ICoordinatorSession session, Func<bool> isInCall)
    {
        Dispatcher.UIThread.VerifyAccess();
        Detach();
        _session = session;
        _isInCall = isInCall;
        _sent = IdleLevel.Active;
        _unsupported = false;
        session.StateChanged += OnStateChanged;
        _timer.Start();
    }

    public void Detach()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_session is not { } session)
            return;
        session.StateChanged -= OnStateChanged;
        _session = null;
        _isInCall = () => false;
        _timer.Stop();
    }

    // The server forgets the level with the connection, so a reconnected one starts active again
    private void OnStateChanged(CoordinatorState state) => Dispatcher.UIThread.Post(() =>
    {
        if (state != CoordinatorState.Connected)
            return;
        _sent = IdleLevel.Active;
        _ = CheckAsync();
    });

    private async Task CheckAsync()
    {
        if (_sending || _unsupported || _session is not { ConnectionStatus: CoordinatorState.Connected } session)
            return;

        var level = IdleLevels.Of(_idle.GetIdleTime(), _settings, _isInCall());
        if (level == _sent)
            return;

        _sending = true;
        try
        {
            var result = await session.SetIdleLevel(level);
            // Tried again on the next check when it failed
            if (result.IsSuccess && _session == session)
                _sent = level;
            else if (result.Error == ErrorCode.UnsupportedRequest)
                _unsupported = true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not send the idle level {Level}", level);
        }
        finally
        {
            _sending = false;
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _behavior.GetSettingsAsync();
            Dispatcher.UIThread.Post(() => _settings = settings);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load the idle status preferences");
        }
    }

    private void OnSettingsChanged(BehaviorSettings settings) => Dispatcher.UIThread.Post(() =>
    {
        _settings = settings;
        _ = CheckAsync();
    });

    public void Dispose()
    {
        _behavior.SettingsChanged -= OnSettingsChanged;
        _timer.Stop();
        if (_session is { } session)
            session.StateChanged -= OnStateChanged;
        _session = null;
    }
}
