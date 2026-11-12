using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Avalonia.Localization;
using Client.Core.Media;
using ReactiveUI;
using Shared.Models.Media;

namespace Client.ViewModels.Sessions.Hybrid;

public partial class ActiveHybridSessionVM
{
    private readonly System.Threading.SemaphoreSlim _broadcastChange = new(1, 1);
    private readonly ICameraDeviceProvider? _cameras;
    private readonly ICameraPreferencesRepository? _cameraPreferences;
    private CameraVideoInput? _cameraInput;
    private ScreenShareSettings? _cameraSettings;
    private readonly ConcurrentDictionary<int, ScreenStreamVM> _cameraStreamsByUser = new();
    private readonly ConcurrentDictionary<int, VideoDecoding> _cameraDecodings = new();

    private void UpdateVideoTexts()
    {
        StatusBar.UpdateTexts();
        foreach (var stream in ScreenStreams) stream.UpdateTexts();
    }

    private async Task RememberBroadcastAsync(bool camera)
    {
        if (_cameraPreferences == null) return;
        var preferences = await _cameraPreferences.GetAsync();
        await _cameraPreferences.SaveAsync(preferences with { LastWasCamera = camera });
        StatusBar.LastWasCamera = camera;
    }

    private async Task ToggleBroadcastAsync()
    {
        var preferences = _cameraPreferences == null ? new CameraPreferences() : await _cameraPreferences.GetAsync();
        StatusBar.LastWasCamera = preferences.LastWasCamera;
        if (preferences.LastWasCamera)
            await ToggleCameraAsync();
        else
            await ToggleScreenShare();
    }

    private async Task ToggleCameraAsync()
    {
        if (!await _broadcastChange.WaitAsync(0)) return;
        StatusBar.IsBroadcastBusy = true;
        try { await ToggleCameraCoreAsync(); }
        finally { StatusBar.IsBroadcastBusy = false; _broadcastChange.Release(); }
    }

    private async Task ToggleCameraCoreAsync()
    {
        if (_cameraInput != null)
        {
            _cameraInput = null;
            _cameraSettings = null;
            await _hybridSession.StopCameraAsync();
            StatusBar.IsCameraOn = false;
            return;
        }
        if (_cameras == null || _cameraPreferences == null) return;
        try
        {
            var preferences = await _cameraPreferences.GetAsync();
            var cameras = await Task.Run(_cameras.GetDevices);
            if (IsClosed) return;
            var codec = Enum.TryParse<VideoCodec>(Model.Media.CameraCodec.ToString(), out var parsed) ? parsed : VideoCodec.VP8;
            var dialog = new VideoSourceSelectWindowVM(null, null, null, false, preferences.CaptureSettings,
                _dismissedPrompts, mediaCapabilities: _mediaCapabilities, codec: codec,
                cameras: cameras, selectedCameraId: preferences.DeviceId);
            ScreenShareRequest? request;
            try { request = await SelectSources.Handle(dialog); }
            catch (UnhandledInteractionException<VideoSourceSelectWindowVM, ScreenShareRequest?>) { return; }
            if (request?.Input is not CameraVideoInput input || IsClosed) return;
            var result = await _hybridSession.StartCameraAsync(input, request.Settings);
            if (IsClosed) return;
            if (!result.IsSuccess)
            {
                if (!result.IsRejected)
                    NotifyScreenShareError(Strings.Format("camera.startFailed", ("reason", result)));
                return;
            }
            _cameraInput = input;
            _cameraSettings = request.Settings;
            StatusBar.IsCameraOn = true;
            StatusBar.LastWasCamera = true;
            await _cameraPreferences.SaveAsync(preferences with
            {
                DeviceId = input.DeviceId, MaxHeight = request.Settings.MaxHeight,
                Framerate = request.Settings.Framerate, BitrateKbps = request.Settings.BitrateKbps,
                LastWasCamera = true
            });
        }
        catch (Exception)
        {
            NotifyScreenShareError(Strings.Get("camera.unavailable"));
        }
    }

    private void OnCameraChanged(int userId, bool active) => Dispatcher.UIThread.Post(() =>
    {
        if (IsClosed) return;
        if (!active)
        {
            RemoveCameraStream(userId);
            return;
        }
        if (_cameraStreamsByUser.ContainsKey(userId)) return;
        var stream = new ScreenStreamVM(userId, userId == _coordinatorSession.GetCurrentUser().Id,
            ResolveDisplayedName(userId), _playbackService, (target, watched) =>
            {
                _hybridSession.SetCameraWatch(userId, watched);
                target.IsWatched = watched;
                if (!watched)
                {
                    if (target.IsExpanded) ToggleExpanded(target);
                    target.Clear();
                }
            }, ToggleExpanded)
        {
            SourceType = MediaSourceType.Camera,
            AvatarUrl = ResolveAvatarUrl(userId),
            IsHidden = ScreenStreams.Any(other => other.IsExpanded),
            IsWatched = _hybridSession.IsWatchingCamera(userId)
        };
        _cameraStreamsByUser[userId] = stream;
        if (_cameraDecodings.TryGetValue(userId, out var decoding)) stream.PushDecoding(decoding);
        ScreenStreams.Add(stream);
        UpdateCanExpand();
    });

    private void RemoveCameraStream(int userId)
    {
        _cameraDecodings.TryRemove(userId, out _);
        if (!_cameraStreamsByUser.TryRemove(userId, out var stream)) return;
        RemoveStreamTile(stream);
    }

    private async Task RestoreCameraAsync()
    {
        if (_cameraInput is not { } input || _cameraSettings is not { } settings || IsClosed) return;
        if (!_hybridSession.IsCameraStarted)
        {
            var result = await _hybridSession.StartCameraAsync(input, settings);
            StatusBar.IsCameraOn = result.IsSuccess;
            if (!result.IsSuccess)
            {
                _cameraInput = null;
                _cameraSettings = null;
                if (!result.IsRejected)
                    NotifyScreenShareError(Strings.Format("camera.startFailed", ("reason", result)));
            }
        }
    }
}
