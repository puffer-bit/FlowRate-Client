using Client.Avalonia.Localization;
using System;
using ReactiveUI;
using Client.Core.Sessions.Hybrid;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Hybrid;

public class HybridSessionStatusBarVM : ReactiveObject
{
    public string StatusBarText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = Strings.Get("hybrid.status.preparingServices");

    /// <summary>
    /// The state alone, for places that already show the session name next to it.
    /// </summary>
    public string ShortStatusText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = Strings.Get("hybrid.status.preparing");

    public bool IsLoading
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>Key of a DrawingImage resource from Client.Avalonia.Assets/IconImages.axaml.</summary>
    public string IconKey
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "IInCircleIcon";

    // The last state shown, to put the new name into its text
    private HybridSessionState? _state;

    public string SessionName
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsUserListVisible
    {
        get;
        set
        {
            ToggleUserListCommand.Execute(value).Subscribe();
            this.RaiseAndSetIfChanged(ref field, value);
        }
    }
    
    /// <summary>
    /// Our microphone is being sent to the session.
    /// </summary>
    public bool IsMicrophoneOn
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    
    /// <summary>
    /// The session is heard. Off while the user silenced all of it, which the others see.
    /// </summary>
    public bool IsSoundOn
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>
    /// Our screen is being sent to the session.
    /// </summary>
    public bool IsScreenShareOn
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleMicrophoneCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleSoundCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleScreenShareCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleFullscreenCommand { get; }
    public ReactiveCommand<bool, RxVoid> ToggleUserListCommand { get; }
    
    public HybridSessionStatusBarVM(
        string sessionName,
        ReactiveCommand<bool, RxVoid> toggleUserListCommand,
        ReactiveCommand<RxVoid, RxVoid> toggleFullscreenCommand,
        ReactiveCommand<RxVoid, RxVoid> closeSessionCommand,
        ReactiveCommand<RxVoid, RxVoid> toggleMicrophoneCommand,
        ReactiveCommand<RxVoid, RxVoid> toggleSoundCommand,
        ReactiveCommand<RxVoid, RxVoid> toggleScreenShareCommand)
    {
        ToggleScreenShareCommand = toggleScreenShareCommand;
        SessionName = sessionName;
        ToggleMicrophoneCommand = toggleMicrophoneCommand;
        ToggleSoundCommand = toggleSoundCommand;
        
        CloseCommand = closeSessionCommand;
        ToggleFullscreenCommand = toggleFullscreenCommand;
        ToggleUserListCommand = toggleUserListCommand;
    }
    
    /// <summary>
    /// Shows the new name of the session, in the status text as well when it names the session.
    /// </summary>
    public void Rename(string sessionName)
    {
        SessionName = sessionName;

        if (_state is HybridSessionState.Connected or HybridSessionState.Failed)
            ChangeStatus(_state.Value);
    }

    public void ChangeStatus(HybridSessionState newState)
    {
        _state = newState;

        switch (newState)
        {
            case HybridSessionState.Connected:
                IsLoading = false;
                IconKey = "MarkIcon";
                StatusBarText = Strings.Format("hybrid.status.connectedTo", ("session", SessionName));
                ShortStatusText = Strings.Get("main.status.connected");
                break;
            case HybridSessionState.Failed:
                IsLoading = false;
                IconKey = "CrossIcon";
                StatusBarText = Strings.Format("hybrid.status.connectionToFailed", ("session", SessionName));
                ShortStatusText = Strings.Get("main.status.connectionFailed");
                break;
            case HybridSessionState.WaitingForNegotiation:
                StatusBarText = Strings.Get("hybrid.status.waitingForRtc");
                ShortStatusText = Strings.Get("hybrid.status.connecting");
                break;
            case HybridSessionState.Reconnecting:
                IsLoading = true;
                StatusBarText = Strings.Get("main.status.connectionLost");
                ShortStatusText = Strings.Get("hybrid.status.reconnecting");
                break;
            case HybridSessionState.WaitingForSfuNode:
                IsLoading = true;
                StatusBarText = Strings.Get("hybrid.status.waitingForVoiceServer");
                ShortStatusText = Strings.Get("hybrid.status.waiting");
                break;
            case HybridSessionState.TimedOut:
                StatusBarText = Strings.Get("main.status.connectionLost");
                ShortStatusText = Strings.Get("hybrid.status.reconnecting");
                break;
            case HybridSessionState.Disconnected:
                StatusBarText = Strings.Get("hybrid.status.disconnected");
                ShortStatusText = Strings.Get("hybrid.status.reconnecting");
                break;
            case HybridSessionState.Canceled:
                IsLoading = false;
                IconKey = "IInCircleIcon";
                StatusBarText = Strings.Get("hybrid.status.canceled");
                ShortStatusText = Strings.Get("hybrid.status.canceled");
                break;
        }
    }
}