using System.Security.Cryptography;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Coordinator;
using Client.Core.Push;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Validation;

namespace Client.Application.Push;

/// <summary>
/// Keeps this device registered for push notifications at the coordinators it is connected to.
/// Does nothing on a platform without a push service.
/// </summary>
public sealed partial class PushRegistration
{
    private readonly IPushTokenProvider? _tokenProvider;
    private readonly IPushKeysRepository _keys;
    private readonly ILogger<PushRegistration> _logger;

    private readonly Lock _sessionsLock = new();
    private readonly List<WeakReference<ICoordinatorSession>> _sessions = [];

    public PushRegistration(IPushKeysRepository keys, ILogger<PushRegistration> logger,
        IPushTokenProvider? tokenProvider = null)
    {
        _keys = keys;
        _logger = logger;
        _tokenProvider = tokenProvider;

        if (_tokenProvider != null)
            _tokenProvider.TokenChanged += OnTokenChanged;
    }

    public void Track(ICoordinatorSession session)
    {
        if (_tokenProvider == null)
            return;

        lock (_sessionsLock)
        {
            _sessions.RemoveAll(reference => !reference.TryGetTarget(out _));
            _sessions.Add(new WeakReference<ICoordinatorSession>(session));
        }

        // Every login, a reconnect included: the server may have lost the device meanwhile
        session.StateChanged += state =>
        {
            if (state == CoordinatorState.Connected)
                _ = RegisterAsync(session);
        };
    }

    /// <summary>
    /// Stops the push notifications of the coordinator the session is connected to, for a user who
    /// leaves it. What is already on its way can no longer be opened.
    /// </summary>
    public async Task UnregisterAsync(ICoordinatorSession session)
    {
        if (_tokenProvider == null || session.ConnectionStatus != CoordinatorState.Connected
                                   || session.CoordinatorModel is not { } coordinator)
            return;

        try
        {
            if (await _tokenProvider.GetTokenAsync() is { } token)
                await session.UnregisterPushToken(token);

            await _keys.RemoveAsync(coordinator.Id);
        }
        catch (Exception exception)
        {
            LogUnregisterFailed(exception, coordinator.Id);
        }
    }

    private void OnTokenChanged(string token)
    {
        List<ICoordinatorSession> sessions = [];

        lock (_sessionsLock)
        {
            foreach (var reference in _sessions)
            {
                if (reference.TryGetTarget(out var session))
                    sessions.Add(session);
            }
        }

        foreach (var session in sessions.Where(session => session.ConnectionStatus == CoordinatorState.Connected))
            _ = RegisterAsync(session);
    }

    private async Task RegisterAsync(ICoordinatorSession session)
    {
        if (session.CoordinatorModel is not { } coordinator)
            return;

        try
        {
            if (await _tokenProvider!.GetTokenAsync() is not { } token)
                return;

            var key = await _keys.GetByCoordinatorAsync(coordinator.Id);
            if (key == null)
            {
                key = new PushKey(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Limits.PushKeyIdLength)),
                    coordinator.Id, RandomNumberGenerator.GetBytes(Limits.PushKeyLength));
                await _keys.SaveAsync(key);
            }

            var (result, replyToken) = await session.RegisterPushToken(token, _tokenProvider.Platform, key);

            if (result.Error is not (null or ErrorCode.FeatureDisabled))
                LogRegisterRefused(coordinator.Id, result.Error);

            // Every registration replaces the token, so the one just given is the only one that works
            if (result.IsSuccess && replyToken != null
                && TrySplitAddress(session.ConnectedAddress, out var address, out var port))
                await _keys.SetReplyTargetAsync(coordinator.Id, new PushReplyTarget(address, port, replyToken));
        }
        catch (Exception exception)
        {
            LogRegisterFailed(exception, coordinator.Id);
        }
    }

    // "address:port"; the port follows the last colon, so an IPv6 address keeps its own
    private static bool TrySplitAddress(string? connectedAddress, out string address, out int port)
    {
        address = string.Empty;
        port = 0;

        var colon = connectedAddress?.LastIndexOf(':') ?? -1;
        if (colon <= 0 || !int.TryParse(connectedAddress.AsSpan(colon + 1), out port))
            return false;

        address = connectedAddress![..colon];
        return true;
    }

    [LoggerMessage(LogLevel.Warning, "Coordinator {CoordinatorId} did not register the device for push: {Result}")]
    private partial void LogRegisterRefused(string coordinatorId, ErrorCode? result);

    [LoggerMessage(LogLevel.Error, "Failed to register the device for push at coordinator {CoordinatorId}")]
    private partial void LogRegisterFailed(Exception exception, string coordinatorId);

    [LoggerMessage(LogLevel.Error, "Failed to stop push notifications of coordinator {CoordinatorId}")]
    private partial void LogUnregisterFailed(Exception exception, string coordinatorId);
}
