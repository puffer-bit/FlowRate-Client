using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Events;
using Client.Core.Settings;
using Client.Application.Sounds;
using Client.ViewModels.Notifications;
using Shared.Enums;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// The microphone and the voices of the session we are in.
/// </summary>
public partial class ActiveHybridSessionVM
{
    // What the running microphone captures from and with, null while it is not started. Settings
    // are saved on every move of a slider, and the capture is rebuilt only when these change.
    private (string? DeviceId, NoiseSuppressionLevel NoiseSuppression)? _microphoneInput;
    private readonly System.Threading.Lock _microphoneInputLock = new();

    /// <summary>
    /// Starts the microphone on a new transport. The voices and the microphone stay the way the user
    /// left them on this coordinator.
    /// </summary>
    private async Task StartVoiceAsync()
    {
        // The server has to be told again: it forgets it with the connection
        if (_hybridSession.IsSoundMuted || await _coordinatorStates.IsSoundMutedAsync(CoordinatorInstanceId))
        {
            await _hybridSession.SetSoundMutedAsync(true);
            StatusBar.IsSoundOn = false;
        }

        var settings = await _audioService.GetSettingsAsync();
        var device = await _audioService.ResolveInputDeviceAsync();
        // The microphone stays off while the voices are: who hears nobody is not heard either
        var isMuted = _hybridSession.IsSoundMuted
                      || await _coordinatorStates.IsMicrophoneMutedAsync(CoordinatorInstanceId);
        var result = await _hybridSession.StartVoiceShareAsync(device?.Id, settings.InputVolume,
            settings.ActiveVoiceGateThresholdDb, settings.NoiseSuppression, isMuted);

        if (result == StartVoiceShareResult.NoError)
        {
            lock (_microphoneInputLock)
                _microphoneInput = (device?.Id, settings.NoiseSuppression);

            StatusBar.IsMicrophoneOn = !_hybridSession.IsMicrophoneMuted;
            return;
        }

        // A rejection is reported by the main window
        if (result != StartVoiceShareResult.Rejected)
            ShowPopUp(NotificationPopUpVM.PopUpType.Error,
                Strings.Format("hybrid.microphone.startFailed", ("reason", result)), "MicrophoneStartFailed");
    }

    private void ForgetMicrophoneInput()
    {
        lock (_microphoneInputLock)
            _microphoneInput = null;
    }

    private void HandleMicroState(PeerMicrophoneMuteChangedEvent notification)
    {
        if (notification.UserId == _coordinatorSession.GetCurrentUser().Id)
        {
            StatusBar.IsMicrophoneOn = !notification.IsMuted;
        }
    }

    /// <summary>
    /// The first press starts the voice; later presses only mute and unmute it, which pauses the
    /// producer on the SFU instead of closing it. What the user chose here is remembered, and is
    /// what the microphone returns to when the voices are turned back on.
    /// </summary>
    private async Task ToggleMicrophone()
    {
        if (!_hybridSession.IsVoiceStarted)
            return;

        // With the voices off the microphone is off because of them, so the press asks to talk
        // again: both come back
        if (_hybridSession.IsSoundMuted)
        {
            await _coordinatorStates.SetMicrophoneMutedAsync(CoordinatorInstanceId, false);
            await ToggleSound();
            return;
        }

        var mute = !_hybridSession.IsMicrophoneMuted;

        if (await SetMicrophoneMutedAsync(mute))
        {
            _sounds.Play(mute ? SoundEvent.Mute : SoundEvent.Unmute);
            await _coordinatorStates.SetMicrophoneMutedAsync(CoordinatorInstanceId, mute);
        }
    }

    /// <returns>False when the microphone stayed as it was, which the user is told about.</returns>
    private async Task<bool> SetMicrophoneMutedAsync(bool mute)
    {
        var muteResult = await _hybridSession.SetMicrophoneMutedAsync(mute);

        // A rejection is reported by the main window
        if (muteResult is not (SetMicrophoneMuteResult.NoError or SetMicrophoneMuteResult.Rejected))
        {
            ShowPopUp(NotificationPopUpVM.PopUpType.Error,
                Strings.Format(mute ? "hybrid.microphone.muteFailed" : "hybrid.microphone.unmuteFailed",
                    ("reason", muteResult)), "MicrophoneMuteFailed");
        }

        return muteResult == SetMicrophoneMuteResult.NoError;
    }

    /// <summary>
    /// Silences the voices of the session, or brings them back; the others see that we do not hear
    /// them. The microphone goes off with the voices and returns to what the user chose for it.
    /// </summary>
    private async Task ToggleSound()
    {
        var mute = !_hybridSession.IsSoundMuted;

        StatusBar.IsSoundOn = !mute;
        _sounds.Play(mute ? SoundEvent.Deafen : SoundEvent.Undeafen);

        var result = await _hybridSession.SetSoundMutedAsync(mute);

        await _coordinatorStates.SetSoundMutedAsync(CoordinatorInstanceId, mute);

        // The sound is switched here anyway; a rejection is reported by the main window
        if (result is not (SetSoundMuteResult.NoError or SetSoundMuteResult.Rejected))
        {
            ShowPopUp(NotificationPopUpVM.PopUpType.Error,
                Strings.Format("hybrid.sound.muteFailed", ("reason", result)), "SoundMuteFailed");
        }

        if (!_hybridSession.IsVoiceStarted)
            return;

        // Not remembered as the choice of the user: the microphone is only off for as long as the voices are
        if (mute)
        {
            if (!_hybridSession.IsMicrophoneMuted)
                await SetMicrophoneMutedAsync(true);
        }
        else if (_hybridSession.IsMicrophoneMuted
                 && !await _coordinatorStates.IsMicrophoneMutedAsync(CoordinatorInstanceId))
        {
            await SetMicrophoneMutedAsync(false);
        }
    }

    private string CoordinatorInstanceId => _coordinatorSession.GetCurrentUser().CoordinatorInstanceId;

    /// <summary>
    /// Applies a change made in the settings to the running microphone.
    /// </summary>
    private void OnAudioSettingsChanged(AudioSettings settings)
    {
        _hybridSession.SetInputVolume(settings.InputVolume);
        _hybridSession.SetVoiceGateThreshold(settings.ActiveVoiceGateThresholdDb);
        _ = ApplyMicrophoneInputAsync(settings);
    }

    /// <summary>
    /// Switches the running microphone to the device and the noise suppression of the settings,
    /// when either differs from what it runs with.
    /// </summary>
    private async Task ApplyMicrophoneInputAsync(AudioSettings settings)
    {
        var device = await _audioService.ResolveInputDeviceAsync();
        var input = (device?.Id, settings.NoiseSuppression);

        lock (_microphoneInputLock)
        {
            if (_microphoneInput == null || _microphoneInput == input)
                return;

            _microphoneInput = input;
        }

        _hybridSession.SetMicrophoneInput(input.Id, input.NoiseSuppression);
    }
}
