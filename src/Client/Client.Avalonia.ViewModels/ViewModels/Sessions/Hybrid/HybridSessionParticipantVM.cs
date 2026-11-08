using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using ReactiveUI;
using Shared.Enums;

namespace Client.ViewModels.Sessions.Hybrid;

public class HybridSessionParticipantVM : ReactiveObject, IParticipantVM
{
    // Peer data
    public PeerModel PeerData { get; private set; }
    public UserModel? UserData { get; private set; }

    public string DisplayedName => UserData == null ? PeerData.UserId.ToString() : UserData.Username;

    public bool IsConnected => PeerData.State == HybridSessionPeerState.Connected;

    /// <summary>The user waits for the host to let them in.</summary>
    public bool IsWaiting => PeerData.State == HybridSessionPeerState.WaitingForApprove;

    /// <summary>
    /// The user speaks right now.
    /// </summary>
    public bool IsSpeaking
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The user shows a screen right now.
    /// </summary>
    public bool IsSharingScreen
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsCurrentUser { get; }

    public bool IsVoiceForbidden
    {
        get;
        set
        {
            if (field == value)
                return;
            if (value)
                _hasMicrophoneState = true;
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ShowsMicrophoneOff));
            this.RaisePropertyChanged(nameof(MicrophoneIconKey));
        }
    }

    public bool ShowsMicrophoneOff => IsVoiceForbidden || (_hasMicrophoneState && PeerData.IsMicrophoneMuted);

    // Joining precedes capture startup; wait for a microphone event before showing an ordinary mute.
    private bool _hasMicrophoneState;

    public string MicrophoneIconKey => IsVoiceForbidden ? "ForbiddenMicrophoneIcon" : "CrossedMicrophoneIcon";

    public void UpdateMicrophoneMuted(bool isMuted, bool isConfirmed = true)
    {
        var stateConfirmed = isConfirmed && !_hasMicrophoneState;
        _hasMicrophoneState |= isConfirmed;
        if (PeerData.IsMicrophoneMuted == isMuted && !stateConfirmed)
            return;
        PeerData.IsMicrophoneMuted = isMuted;
        this.RaisePropertyChanged(nameof(ShowsMicrophoneOff));
    }

    /// <summary>The roles of the current user, or hosting the session, let it remove this user.</summary>
    public bool CanKick
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The user waits to be let in, and the roles of the current user, or hosting the session, let it decide.</summary>
    public bool CanApprove
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public HybridSessionParticipantVM(PeerModel peerData, bool isCurrentUser, UserModel? userData = null)
    {
        PeerData = peerData;
        _hasMicrophoneState = peerData.State == HybridSessionPeerState.Connected;
        IsCurrentUser = isCurrentUser;
        UserData = userData;
        IsSharingScreen = peerData.IsScreenShareActive;
    }

    public void UpdateUserModel(UserModel? userModel)
    {
        UserData = userModel;
        this.RaisePropertyChanged(nameof(UserData));
        this.RaisePropertyChanged(nameof(DisplayedName));
    }

    public void UpdatePeerModel(PeerModel peerModel)
    {
        UpdatePeerStatus(peerModel.State);
        UpdateMicrophoneMuted(peerModel.IsMicrophoneMuted, isConfirmed: peerModel.State == HybridSessionPeerState.Connected);
        IsSharingScreen = peerModel.IsScreenShareActive;
        PeerData = peerModel;
        this.RaisePropertyChanged(nameof(PeerData));
    }

    public void UpdatePeerStatus(HybridSessionPeerState newState)
    {
        if (PeerData.State == newState)
            return;
        PeerData.State = newState;
        if (newState != HybridSessionPeerState.Connected)
        {
            _hasMicrophoneState = IsVoiceForbidden;
            IsSpeaking = false;
            UpdateMicrophoneMuted(true, isConfirmed: false);
        }
        this.RaisePropertyChanged(nameof(PeerData));
        this.RaisePropertyChanged(nameof(IsConnected));
        this.RaisePropertyChanged(nameof(IsWaiting));
        this.RaisePropertyChanged(nameof(ShowsMicrophoneOff));
    }
}
