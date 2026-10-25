using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Application.Sounds;
using Client.Core.Events;
using Client.Services;
using Client.ViewModels.Notifications;
using Shared.Enums;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// Being away from the keyboard in the session we are in, and the pokes of the others.
/// </summary>
public partial class ActiveHybridSessionVM
{
    /// <summary>
    /// Tells the server again that we are away, on a new transport: it forgets it with the connection.
    /// Away stays the way the user left it on this coordinator.
    /// </summary>
    private async Task RestoreAfkAsync()
    {
        var (isAfk, reason) = _hybridSession.IsAfk
            ? (true, _hybridSession.AfkReason)
            : await _coordinatorStates.GetAfkAsync(CoordinatorInstanceId);

        if (!isAfk)
            return;

        await _hybridSession.SetAfkAsync(true, reason);
        ShowAfk();
    }

    /// <summary>
    /// Goes away with the reason typed in the bar, or comes back. Remembered on this coordinator, as the mutes are.
    /// The microphone goes off while away and returns to what the user chose for it, unless the voices keep it off.
    /// </summary>
    private async Task ToggleAfk()
    {
        var isAfk = !_hybridSession.IsAfk;
        var result = await _hybridSession.SetAfkAsync(isAfk, isAfk ? StatusBar.AfkReasonDraft : null);

        ShowAfk();
        await _coordinatorStates.SetAfkAsync(CoordinatorInstanceId, isAfk, _hybridSession.AfkReason);

        // Kept here anyway; a rejection is reported by the main window
        if (result is not (SetAfkResult.NoError or SetAfkResult.Rejected))
            ShowPopUp(NotificationPopUpVM.PopUpType.Error, Strings.Format("hybrid.afk.failed", ("reason", result)),
                "AfkFailed");

        if (!_hybridSession.IsVoiceStarted)
            return;

        // Not remembered as the choice of the user: the microphone is only off for as long as the user is away
        if (isAfk)
        {
            if (!_hybridSession.IsMicrophoneMuted && await SetMicrophoneMutedAsync(true))
                _sounds.Play(SoundEvent.Mute);
        }
        else if (!_hybridSession.IsSoundMuted && _hybridSession.IsMicrophoneMuted)
        {
            await RestoreMicrophoneAsync();
            if (!_hybridSession.IsMicrophoneMuted)
                _sounds.Play(SoundEvent.Unmute);
        }
    }

    private void ShowAfk()
    {
        StatusBar.IsAfk = _hybridSession.IsAfk;
        StatusBar.AfkReason = _hybridSession.AfkReason;

        // The next time the user goes away, the last reason is offered again
        if (_hybridSession.AfkReason is { } reason)
            StatusBar.AfkReasonDraft = reason;
    }

    // Raised on socket threads
    private void OnPoked(PeerPokedEvent poked)
    {
        if (poked.SessionId == Model.Id)
            Dispatcher.UIThread.Post(async () => await ShowPokeAsync(poked));
    }

    private async Task ShowPokeAsync(PeerPokedEvent poked)
    {
        if (IsClosed)
            return;

        var initiator = await _coordinatorSession.GetUser(poked.InitiatorUserId);
        var name = initiator?.Username ?? poked.InitiatorUserId.ToString();

        _sounds.Play(SoundEvent.Mention);
        _notifications.Show(NotificationType.Info, Strings.Format("hybrid.poked.title", ("user", name)),
            poked.Message ?? Strings.Get("hybrid.poked.noMessage"));
    }
}
