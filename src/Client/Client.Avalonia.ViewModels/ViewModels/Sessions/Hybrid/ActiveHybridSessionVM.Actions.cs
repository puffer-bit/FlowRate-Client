using Client.Avalonia.Styles.Animation;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Sounds;
using Client.Avalonia.Localization;
using Client.Core.Media;
using Client.Core.Users;
using ReactiveUI;
using ReactiveUI.Primitives;
using Shared.Enums;
using Shared.Models.Media;
using Client.ViewModels.Notifications;

namespace Client.ViewModels.Sessions.Hybrid;

public partial class ActiveHybridSessionVM
{
    // What we show, kept for as long as the share lasts, so that it can be started again after a reconnect
    private VideoInput? _screenInput;

    // How we show it, as it was set up in the dialog; kept together with the input
    private ScreenShareSettings? _screenSettings;

    // The sound sent along, or null for a silent share; kept together with the input
    private AudioInput? _screenAudioInput;

    // The dialog that starts a share, while it is open
    private VideoSourceSelectWindowVM? _sourceSelect;

    // The same screens as in ScreenStreams by user, read on the threads of the video tracks
    private readonly ConcurrentDictionary<int, ScreenStreamVM> _screenStreamsByUser = new();

    // Whose screens the user asked to watch, kept across a reconnect of the transport. UI thread only
    private readonly HashSet<int> _wantedScreens = [];

    private async Task ToggleScreenShare()
    {
        if (_screenInput != null)
        {
            await StopScreenShareAsync();
            return;
        }

        var (request, _) = await SelectScreenShareAsync(null, await _screenShareSettings.GetAsync());

        if (request == null)
            return;

        _screenInput = request.Input;
        _screenSettings = request.Settings;
        _screenAudioInput = request.AudioInput;

        if (IsClosed)
        {
            ReleaseScreenInput();
            return;
        }

        await StartScreenShareAsync(request.Input, request.Settings, request.AudioInput);

        // Offered again by the next dialog
        await _screenShareSettings.SaveAsync(request.Settings);
    }

    /// <summary>
    /// Changes what the running share shows and how, in the same dialog that started it. The
    /// viewers keep watching it through the change.
    /// </summary>
    private async Task ChangeScreenShare()
    {
        if (_screenInput is not { } sharedInput || _screenSettings is not { } sharedSettings)
            return;

        var (request, isSharedInputLost) = await SelectScreenShareAsync(sharedInput, sharedSettings);

        // Stopped or changed meanwhile, by the system or by a reconnect that failed to bring it back
        if (_screenInput != sharedInput)
        {
            if (request != null && request.Input != sharedInput)
                ReleaseInput(request.Input, closeCast: _screenInput == null);

            return;
        }

        if (request == null)
        {
            // The system took the source away for a new pick that was given up
            if (isSharedInputLost)
                await StopScreenShareAsync();

            return;
        }

        if (IsClosed)
        {
            if (request.Input != sharedInput)
                ReleaseInput(request.Input, closeCast: false);

            return;
        }

        _screenInput = request.Input;
        _screenSettings = request.Settings;
        _screenAudioInput = request.AudioInput;

        var result = await _hybridSession.ChangeScreenShareAsync(request.Input, request.Settings, request.AudioInput);

        // The track was rebuilt on the new source, so the old one can go; the system has closed it already
        if (request.Input != sharedInput)
            ReleaseInput(sharedInput, closeCast: false);

        if (IsClosed)
            return;

        if (result == StartScreenShareResult.NoError)
            StatusBar.IsScreenShareOn = true;
        else
        {
            // The session has stopped the share already
            _sounds.Play(SoundEvent.ScreenShareStop);
            StatusBar.IsScreenShareOn = false;
            ReleaseScreenInput();

            if (result != StartScreenShareResult.Rejected)
                NotifyScreenShareError(Strings.Format("hybrid.screen.startFailed", ("reason", result)));
        }

        await _screenShareSettings.SaveAsync(request.Settings);
    }

    /// <summary>
    /// Asks what to show and how, in the dialog of the session.
    /// </summary>
    /// <param name="sharedInput">What the running share shows, when the dialog changes it.</param>
    /// <returns>
    /// Null when the user changed their mind or nothing can be captured, and whether the system
    /// closed the source of the running share on the way.
    /// </returns>
    private async Task<(ScreenShareRequest? Request, bool IsSharedInputLost)> SelectScreenShareAsync(
        VideoInput? sharedInput, ScreenShareSettings settings)
    {
        if (_screenCastClient == null && _screenSourceProvider == null)
            return (null, false);

        var dialog = new VideoSourceSelectWindowVM(_screenSourceProvider, _screenCastClient,
            _mediaCapabilities.CanCaptureScreenAudio ? _audioApps : null, _mediaCapabilities.CanCaptureScreenAudio,
            settings, _dismissedPrompts, sharedInput);
        ScreenShareRequest? request = null;

        _sourceSelect = dialog;

        try
        {
            request = await SelectSources.Handle(dialog);
        }
        catch (UnhandledInteractionException<VideoSourceSelectWindowVM, ScreenShareRequest?>)
        {
            // No window to show the dialog in
        }
        finally
        {
            _sourceSelect = null;
        }

        if (request == null)
            dialog.Abandon();

        return (request, dialog.IsSharedInputLost);
    }

    private async Task StartScreenShareAsync(VideoInput input, ScreenShareSettings settings, AudioInput? audioInput)
    {
        var result = await _hybridSession.StartScreenShareAsync(input, settings, audioInput);

        if (IsClosed)
            return;

        if (result == StartScreenShareResult.NoError)
        {
            StatusBar.IsScreenShareOn = true;
            _sounds.Play(SoundEvent.ScreenShareStart);
            return;
        }

        ReleaseScreenInput();

        // A rejection is reported by the main window
        if (result != StartScreenShareResult.Rejected)
            NotifyScreenShareError(Strings.Format("hybrid.screen.startFailed", ("reason", result)));
    }

    private async Task StopScreenShareAsync()
    {
        if (StatusBar.IsScreenShareOn)
            _sounds.Play(SoundEvent.ScreenShareStop);

        StatusBar.IsScreenShareOn = false;

        // The track goes first: closing the source under a running capture makes it report an error
        await _hybridSession.StopScreenShareAsync();

        ReleaseScreenInput();
    }

    /// <summary>
    /// Gives the source back to the system, which then stops showing that the screen is shared.
    /// </summary>
    private void ReleaseScreenInput()
    {
        var input = _screenInput;
        _screenInput = null;
        _screenSettings = null;
        _screenAudioInput = null;

        if (input != null)
            ReleaseInput(input, closeCast: true);
    }

    /// <param name="closeCast">Ends the capture of the system as well, which holds one source at a time.</param>
    private void ReleaseInput(VideoInput input, bool closeCast)
    {
        if (input is PipeWireVideoInput pipeWire)
            pipeWire.Remote.Dispose();

        if (closeCast && _screenCastClient != null)
            _ = _screenCastClient.CloseAsync();
    }

    // Raised on a thread of the portal connection
    private void OnScreenCastClosed() => Dispatcher.UIThread.Post(async () =>
    {
        // A source that was picked but not started yet is gone as well
        _sourceSelect?.DropSystemInput();

        if (!IsClosed && _screenInput != null)
            await StopScreenShareAsync();
    });

    // Raised on any thread, for the others and for ourselves
    private void OnScreenShareChanged(int userId, bool isSharing) => Dispatcher.UIThread.Post(() =>
    {
        if (IsClosed)
            return;

        ParticipantsListVM.UpdateScreenShare(userId, isSharing);

        // Our own share sounds where it is started and stopped; a repeated report changes nothing
        if (userId != _coordinatorSession.GetCurrentUser().Id && _screenStreamsByUser.ContainsKey(userId) != isSharing)
            _sounds.Play(isSharing ? SoundEvent.ScreenShareStart : SoundEvent.ScreenShareStop);

        if (isSharing)
            AddScreenStream(userId);
        else
            RemoveScreenStream(userId);
    });

    private void AddScreenStream(int userId)
    {
        if (_screenStreamsByUser.ContainsKey(userId))
            return;

        var stream = new ScreenStreamVM(userId, userId == _coordinatorSession.GetCurrentUser().Id,
            ResolveDisplayedName(userId), _playbackService, (target, isWatched) => _ = SetScreenWatchedAsync(target.UserId, isWatched),
            ToggleExpanded)
        {
            // Another screen is expanded over the surface right now
            AvatarUrl = ResolveAvatarUrl(userId),
            IsHidden = ScreenStreams.Any(other => other.IsExpanded),
            IsWatched = _hybridSession.IsWatchingScreen(userId)
        };

        _screenStreamsByUser[userId] = stream;
        ScreenStreams.Add(stream);
        UpdateCanExpand();
    }

    private void RemoveScreenStream(int userId)
    {
        _wantedScreens.Remove(userId);

        if (!_screenStreamsByUser.TryRemove(userId, out var stream))
            return;

        // Nothing to fade while another screen covers this one. It is taken out before the last
        // screen is brought back from being expanded, which would show it again for its fade
        if (stream.IsHidden)
        {
            stream.Clear();
            ScreenStreams.Remove(stream);
            UpdateCanExpand();
            return;
        }

        if (stream.IsExpanded)
            ToggleExpanded(stream);

        UpdateCanExpand();

        // The tile fades away with its last frame still on it, and is only taken out after that
        stream.IsLeaving = true;
        DispatcherTimer.RunOnce(() =>
        {
            stream.Clear();
            ScreenStreams.Remove(stream);
        }, Presence.LeaveTime);
    }

    private string ResolveDisplayedName(int userId) =>
        ParticipantsListVM.Participants.FirstOrDefault(participant => participant.PeerData.UserId == userId)
            ?.DisplayedName ?? userId.ToString();

    private string? ResolveAvatarUrl(int userId) =>
        ParticipantsListVM.Participants.FirstOrDefault(participant => participant.PeerData.UserId == userId)
            ?.UserData?.AvatarUrl;

    private void UpdateScreenStreamName(int userId)
    {
        if (!_screenStreamsByUser.TryGetValue(userId, out var stream))
            return;

        stream.DisplayedName = ResolveDisplayedName(userId);
        stream.AvatarUrl = ResolveAvatarUrl(userId);
    }

    /// <summary>
    /// Starts or stops watching the screen of a user, from its tile or from a list of peers.
    /// </summary>
    private async Task SetScreenWatchedAsync(int userId, bool isWatched)
    {
        if (IsClosed || !_screenStreamsByUser.TryGetValue(userId, out var stream) || stream.IsBusy)
            return;

        if (isWatched)
            _wantedScreens.Add(userId);
        else
            _wantedScreens.Remove(userId);

        stream.IsBusy = true;

        SetScreenShareWatchResult result;

        try
        {
            result = await _hybridSession.SetScreenWatchAsync(userId, isWatched);
        }
        finally
        {
            stream.IsBusy = false;
        }

        if (IsClosed || result == SetScreenShareWatchResult.NoError)
            return;

        if (isWatched)
            _wantedScreens.Remove(userId);

        // A rejection is reported by the main window
        if (isWatched && result != SetScreenShareWatchResult.Rejected)
            NotifyScreenShareError(Strings.Format("hybrid.screen.watchFailed", ("reason", result)));
    }

    /// <summary>
    /// Asks again for the screens that were watched before the transport was rebuilt.
    /// </summary>
    private async Task RestoreWatchedScreensAsync()
    {
        foreach (var userId in _wantedScreens.ToArray())
        {
            if (!_hybridSession.IsWatchingScreen(userId))
                await SetScreenWatchedAsync(userId, true);
        }
    }

    // Raised on any thread
    private void OnScreenWatchChanged(int userId, bool isWatched) => Dispatcher.UIThread.Post(() =>
    {
        if (IsClosed || !_screenStreamsByUser.TryGetValue(userId, out var stream))
            return;

        stream.IsWatched = isWatched;

        if (isWatched)
            return;

        if (stream.IsExpanded)
            ToggleExpanded(stream);

        stream.Clear();
    });

    // Raised on any thread, also by the slider of a tile of this very session
    // A tile of a screen controls the sound of that screen only, the voice is set in the lists of peers
    private void OnPlaybackChanged(int userId, PeerAudioChannel channel, PeerPlayback playback) =>
        Dispatcher.UIThread.Post(() =>
    {
        if (channel == PeerAudioChannel.Screen && !IsClosed && _screenStreamsByUser.TryGetValue(userId, out var stream))
            stream.ApplyPlayback(playback);
    });

    // Raised on the UI thread by a list of peers, for any session
    private void OnScreenWatchRequested(int sessionId, int userId, bool isWatched)
    {
        if (sessionId == Model.Id)
            Dispatcher.UIThread.Post(() => _ = SetScreenWatchedAsync(userId, isWatched));
    }

    /// <summary>
    /// Lets one screen take the whole surface, or brings the others back.
    /// </summary>
    private void ToggleExpanded(ScreenStreamVM stream)
    {
        // A double click on the picture gets here past the command
        if (!stream.IsExpanded && !stream.CanExpand)
            return;

        var isExpanded = !stream.IsExpanded;

        foreach (var other in ScreenStreams)
        {
            other.IsExpanded = isExpanded && other == stream;
            other.IsHidden = isExpanded && other != stream;
        }
    }

    /// <summary>
    /// A screen can be expanded while there is another one to hide behind it. The last screen
    /// left is brought back from being expanded: it has the whole surface anyway.
    /// </summary>
    private void UpdateCanExpand()
    {
        var canExpand = _screenStreamsByUser.Count > 1;

        foreach (var stream in _screenStreamsByUser.Values)
        {
            if (!canExpand && stream.IsExpanded)
                ToggleExpanded(stream);

            stream.CanExpand = canExpand;
        }
    }

    // Raised on a thread of the track, and the pixels are only valid during the call
    private void OnVideoFrame(int userId, MediaSourceType source, RawVideoFrame frame)
    {
        if (source == MediaSourceType.Screen && _screenStreamsByUser.TryGetValue(userId, out var stream))
            stream.PushFrame(frame);
    }

    private void NotifyScreenShareError(string text) =>
        ShowPopUp(NotificationPopUpVM.PopUpType.Error, text, "ScreenShareFailed");
}
