using System.Collections.Concurrent;
using Client.Application.Ports;
using Client.Core.Coordinator;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Client.Application.Sessions.Hybrid;

public partial class HybridSession
{
    private OutboundTrack? _cameraOutbound;
    private readonly SemaphoreSlim _cameraChange = new(1, 1);
    private readonly ConcurrentDictionary<int, bool> _cameraUsers = new();
    public bool IsCameraStarted => _cameraOutbound != null;
    public IReadOnlyCollection<int> CameraUsers => _cameraUsers.Keys.ToArray();
    public event Action<int, bool>? CameraChanged;

    public async Task<RequestResult> StartCameraAsync(CameraVideoInput input, ScreenShareSettings settings)
    {
        await _cameraChange.WaitAsync();
        try { return await StartCameraCoreAsync(input, settings); }
        finally { _cameraChange.Release(); }
    }

    private async Task<RequestResult> StartCameraCoreAsync(CameraVideoInput input, ScreenShareSettings settings)
    {
        if (IsDisposed) return ErrorCode.Internal;
        if (IsCameraStarted)
            return RequestResult.Success;
        var transport = _webRTCService;
        var (result, outbound) = await _coordinatorSession.StartVideoShareInHybridSession(Model.Id);
        if (!result.IsSuccess)
            return result;
        if (outbound == null || transport != _webRTCService || IsDisposed)
            return ErrorCode.Internal;
        try
        {
            if (await Task.Run(() => transport.StartOutbound(outbound, input,
                    settings with { IsAudioEnabled = false, AudioAppKey = null })))
            {
                lock (_reconnectLock)
                {
                    if (transport == _webRTCService && !IsDisposed)
                    {
                        _cameraOutbound = outbound;
                        SetCameraActive(outbound.UserId, true);
                        return RequestResult.Success;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Camera capture could not start in session {SessionId}", Model.Id);
        }
        transport.StopOutbound(outbound.Id);
        await _coordinatorSession.StopVideoShareInHybridSession(Model.Id);
        return ErrorCode.UnsupportedMedia;
    }

    public async Task<RequestResult> StopCameraAsync()
    {
        await _cameraChange.WaitAsync();
        try { return await StopCameraCoreAsync(); }
        finally { _cameraChange.Release(); }
    }

    private async Task<RequestResult> StopCameraCoreAsync()
    {
        var outbound = Interlocked.Exchange(ref _cameraOutbound, null);
        if (outbound == null)
            return RequestResult.Success;
        _webRTCService.StopOutbound(outbound.Id);
        SetCameraActive(outbound.UserId, false);
        return await _coordinatorSession.StopVideoShareInHybridSession(Model.Id);
    }

    private async Task HandleCameraCaptureFailedAsync(IWebRTCTransport transport, string outboundId)
    {
        await _cameraChange.WaitAsync();
        try
        {
            if (IsDisposed || transport != _webRTCService || _cameraOutbound?.Id != outboundId) return;
            await StopCameraCoreAsync();
            ErrorOccurred?.Invoke(new HybridSessionError(HybridSessionErrorKind.CameraCaptureFailed));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to stop camera in session {SessionId}", Model.Id);
        }
        finally { _cameraChange.Release(); }
    }

    public bool IsWatchingCamera(int userId) => _cameraUsers.TryGetValue(userId, out var watched) && watched;

    public void SetCameraWatch(int userId, bool isWatching)
    {
        if (!_cameraUsers.TryGetValue(userId, out var previous) ||
            !_cameraUsers.TryUpdate(userId, isWatching, previous))
            return;
        if (_cameraOutbound is { } outbound && outbound.UserId == userId)
            _webRTCService.SetOutboundPreviewShown(outbound.Id, isWatching);
    }

    private void SetCameraActive(int userId, bool active)
    {
        if (active ? _cameraUsers.TryAdd(userId, true) : _cameraUsers.TryRemove(userId, out _))
            CameraChanged?.Invoke(userId, active);
    }
}
