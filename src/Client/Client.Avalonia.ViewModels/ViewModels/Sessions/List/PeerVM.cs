using System;
using ReactiveUI;
using Shared.Enums;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.Application.Coordinator.Users;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

public class PeerVM : ReactiveObject
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

    /// <summary>The user waits for the host to let them in.</summary>
    public bool IsWaiting
    {
        get => PeerData.State == HybridSessionPeerState.WaitingForApprove;
    }

    public bool IsCurrentUser { get; }

    /// <summary>
    /// The peer is playing its leave animation and is removed from the list right after it.
    /// </summary>
    public bool IsLeaving
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The user turned their own microphone off.
    /// </summary>
    public bool IsMicrophoneMuted => PeerData.IsMicrophoneMuted;

    /// <summary>
    /// The user turned the sound of the session off and hears nobody.
    /// </summary>
    public bool IsDeafened => PeerData.IsSoundMuted;

    /// <summary>
    /// The user shows a screen right now.
    /// </summary>
    public bool IsSharingScreen => PeerData.IsScreenShareActive;

    /// <summary>
    /// We watch the screen of the user. Nobody's screen is received until it is asked for.
    /// </summary>
    public bool IsWatchingScreen
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The user shows a screen in the session we are in, so it can be watched from here.
    /// </summary>
    public bool CanWatchScreen => IsSharingScreen && IsInEnteredSession;

    /// <summary>
    /// The peer belongs to the session we are in.
    /// </summary>
    public bool IsInEnteredSession
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanWatchScreen));
        }
    }

    /// <summary>
    /// The user speaks right now. Known only for the session we are in.
    /// </summary>
    public bool IsSpeaking
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Playback of this user, applied locally only
    public double Volume
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(VolumePercentText));
            this.RaisePropertyChanged(nameof(IsAmplified));
            PublishPlayback();
        }
    } = PeerPlayback.Default.Volume;

    // Linear gain shown as percent: 1.0 is 100%, overdrive goes up to 200%
    public string VolumePercentText => $"{Math.Round(Volume * 100)}%";

    // Above 100% the sound is amplified past the original
    public bool IsAmplified => Math.Round(Volume * 100) > 100;

    public bool IsMuted
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            PublishPlayback();
        }
    }

    public bool IsOverdriveEnabled
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(MaxVolume));

            if (!value && Volume > MaxVolume)
                Volume = MaxVolume;

            PublishPlayback();
        }
    }

    public ReactiveCommand<int, RxVoid> ViewProfileCommand { get; }

    public double MaxVolume => IsOverdriveEnabled ? PeerPlayback.OverdriveMaxVolume : PeerPlayback.NormalMaxVolume;

    private readonly IPeerPlaybackService _playbackService;
    private readonly Action<PeerVM> _toggleScreenWatch;
    private readonly Action<PeerVM> _kick;
    private readonly Action<PeerVM> _ban;
    private readonly Action<PeerVM, bool> _admit;

    // Set while the settings come from the service, so they are not sent back to it
    private bool _isApplyingPlayback;

    public PeerVM(PeerModel peerData, bool isCurrentUser, IPeerPlaybackService playbackService,
        ReactiveCommand<int, RxVoid> viewProfileCommand, Action<PeerVM> toggleScreenWatch, Action<PeerVM> kick,
        Action<PeerVM> ban, Action<PeerVM, bool> admit, UserModel? userData = null)
    {
        _toggleScreenWatch = toggleScreenWatch;
        _kick = kick;
        _ban = ban;
        _admit = admit;
        ViewProfileCommand = viewProfileCommand;
        PeerData = peerData;
        IsCurrentUser = isCurrentUser;
        UserData = userData;
        _playbackService = playbackService;

        ApplyPlayback(playbackService.Get(peerData.UserId, PeerAudioChannel.Voice));
    }

    /// <summary>
    /// Shows settings changed elsewhere: loaded from the storage or changed in another list.
    /// </summary>
    public void ApplyPlayback(PeerPlayback playback)
    {
        _isApplyingPlayback = true;

        try
        {
            // The overdrive goes first, otherwise the slider would cut a louder volume down to its old maximum
            IsOverdriveEnabled = playback.IsOverdriveEnabled;
            Volume = playback.Volume;
            IsMuted = playback.IsMuted;
        }
        finally
        {
            _isApplyingPlayback = false;
        }
    }

    private void PublishPlayback()
    {
        if (_isApplyingPlayback || IsCurrentUser)
            return;

        _playbackService.Set(PeerData.UserId, PeerAudioChannel.Voice, new PeerPlayback(Volume, IsMuted, IsOverdriveEnabled));
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    /// <summary>
    /// Starts watching the screen of the user, or stops it.
    /// </summary>
    public void ToggleScreenWatch()
    {
        if (CanWatchScreen)
            _toggleScreenWatch(this);
    }

    /// <summary>
    /// The roles of the current user, or hosting the session, let it remove this user. Never for ourselves.
    /// </summary>
    public bool CanKick
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Removes the user from the session. Offered only when <see cref="CanKick"/>.
    /// </summary>
    public void Kick()
    {
        if (CanKick)
            _kick(this);
    }

    /// <summary>
    /// The roles of the current user let it ban this user from the coordinator. Never for ourselves.
    /// </summary>
    public bool CanBan
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Bans the user from the coordinator, which takes it out of every session. Offered only when <see cref="CanBan"/>.
    /// </summary>
    public void Ban()
    {
        if (CanBan)
            _ban(this);
    }

    /// <summary>
    /// The user waits to be let in, and the roles of the current user, or hosting the session, let it decide.
    /// </summary>
    public bool CanApprove
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Lets the waiting user in. Offered only when <see cref="CanApprove"/>.</summary>
    public void Approve()
    {
        if (CanApprove)
            _admit(this, true);
    }

    /// <summary>Turns the waiting user away, which takes them out. Offered only when <see cref="CanApprove"/>.</summary>
    public void Reject()
    {
        if (CanApprove)
            _admit(this, false);
    }

    public void ToggleOverdrive()
    {
        IsOverdriveEnabled = !IsOverdriveEnabled;
    }
    
    public void UpdateUserModel(UserModel? userModel)
    {
        UserData = userModel;
        this.RaisePropertyChanged(nameof(DisplayedName));
        this.RaisePropertyChanged(nameof(UserData));
    }

    public void UpdateMicrophoneMuted(bool isMuted)
    {
        PeerData.IsMicrophoneMuted = isMuted;
        this.RaisePropertyChanged(nameof(IsMicrophoneMuted));
    }

    public void UpdateSoundMuted(bool isMuted)
    {
        PeerData.IsSoundMuted = isMuted;
        this.RaisePropertyChanged(nameof(IsDeafened));
    }

    public void UpdateScreenShare(bool isActive)
    {
        PeerData.IsScreenShareActive = isActive;
        this.RaisePropertyChanged(nameof(IsSharingScreen));
        this.RaisePropertyChanged(nameof(CanWatchScreen));
    }

    public void UpdatePeerStatus(HybridSessionPeerState newState)
    {
        PeerData.State = newState;

        // The server unmutes a peer that loses its connection, and its screen is gone with it
        if (newState != HybridSessionPeerState.Connected)
        {
            UpdateMicrophoneMuted(false);
            UpdateSoundMuted(false);
            UpdateScreenShare(false);
        }

        this.RaisePropertyChanged(nameof(PeerData));
        this.RaisePropertyChanged(nameof(IsConnected));
        this.RaisePropertyChanged(nameof(IsWaiting));
    }
}