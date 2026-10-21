using ReactiveUI;
using Shared.Enums;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;

namespace Client.ViewModels.Sessions.Hybrid;

public class HybridSessionParticipantVM : ReactiveObject, IParticipantVM
{
    // Peer data
    public PeerModel PeerData { get; private set; }
    public UserModel? UserData { get; private set; }

    public string DisplayedName
    {
        get => UserData == null ? PeerData.UserId.ToString() : UserData.Username;
    }

    public bool IsConnected
    {
        get => PeerData.State == HybridSessionPeerState.Connected;
    }
    
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

    /// <summary>The roles of the current user, or hosting the session, let it remove this user.</summary>
    public bool CanKick
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public HybridSessionParticipantVM(PeerModel peerData, bool isCurrentUser, UserModel? userData = null)
    {
        PeerData = peerData;
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

    public void UpdatePeerStatus(HybridSessionPeerState newState)
    {
        PeerData.State = newState;
        this.RaisePropertyChanged(nameof(PeerData));
        this.RaisePropertyChanged(nameof(IsConnected));
    }
}