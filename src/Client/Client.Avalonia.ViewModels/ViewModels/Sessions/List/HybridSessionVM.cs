using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Primitives;
using Shared.Enums;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.Application.Coordinator.Users;
using Client.Application.Sessions.Hybrid;
using Client.Avalonia.Styles.Animation;
using Client.ViewModels.Dialogs;
using Result = Client.ViewModels.Dialogs.Result;

namespace Client.ViewModels.Sessions.List;

public class HybridSessionVM : ReactiveObject, ISessionVM
{
    // Session data
    public SessionModel Model
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // UI
    public bool IsEntered
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);

            foreach (var peer in Peers)
                peer.IsInEnteredSession = value;
        }
    }

    public bool IsShown
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsLeaving
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsFolded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDragged
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IReadOnlyList<MoveTargetVM> MoveTargets { get; set; } = [];

    /// <summary>The current user hosts the session.</summary>
    public bool IsHost
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The current user may change and delete the session: as its host, or by role.</summary>
    public bool CanManage
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The roles of the current user let it move the session to another category.</summary>
    public bool CanArrange
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // What the current user may do, as of the last ApplyAccess
    private CoordinatorPermissions _permissions = CoordinatorPermissions.None;
    
    public bool IsCorrupted
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public ObservableCollection<PeerVM> Peers { get; set; } = new();

    public PeerVM? SelectedPeer
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private readonly int _currentUserId;
    private readonly IPeerPlaybackService _playbackService;
    private readonly IScreenWatchService _screenWatch;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;
    private readonly ReactiveCommand<(int SessionId, int UserId), RxVoid> _kickPeerCommand;
    private readonly ReactiveCommand<(int SessionId, int UserId, bool IsApproved), RxVoid> _admitPeerCommand;
    
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; }
    
    // Reactive commands
    public ReactiveCommand<int, RxVoid> DeleteSessionCommand { get; set; }
    public ReactiveCommand<SessionModel, RxVoid> ShowSessionCommand { get; set; }
    public ReactiveCommand<int, RxVoid> LeaveSessionCommand { get; set; }
    public ReactiveCommand<SessionModel, RxVoid> EditSessionCommand { get; }
    public ReactiveCommand<SessionModel, RxVoid> EditPermissionsCommand { get; }
    
    public HybridSessionVM(
        HybridSessionModel model,
        int currentUserId,
        IPeerPlaybackService playbackService,
        IScreenWatchService screenWatch,
        ReactiveCommand<SessionModel, RxVoid> showSessionCommand,
        ReactiveCommand<int, RxVoid> leaveSessionCommand,
        ReactiveCommand<SessionModel, RxVoid> editSessionCommand,
        ReactiveCommand<SessionModel, RxVoid> editPermissionsCommand,
        ReactiveCommand<int, RxVoid> deleteSessionCommand,
        ReactiveCommand<int, RxVoid> viewProfileCommand,
        ReactiveCommand<(int SessionId, int UserId), RxVoid> kickPeerCommand,
        ReactiveCommand<(int SessionId, int UserId, bool IsApproved), RxVoid> admitPeerCommand)
    {
        Model = model;

        _currentUserId = currentUserId;
        _playbackService = playbackService;
        _screenWatch = screenWatch;
        _viewProfileCommand = viewProfileCommand;
        _kickPeerCommand = kickPeerCommand;
        _admitPeerCommand = admitPeerCommand;

        ShowMessageBox = new Interaction<MessageBoxVM, Result>();

        ShowSessionCommand = showSessionCommand;
        LeaveSessionCommand = leaveSessionCommand;
        EditSessionCommand = editSessionCommand;
        EditPermissionsCommand = editPermissionsCommand;
        DeleteSessionCommand = deleteSessionCommand;
    }
    
    /// <summary>
    /// Joins this session, or brings it back on screen when we are already in it.
    /// </summary>
    public void Connect()
    {
        if (!CanJoin && !IsEntered)
            return;

        ShowSessionCommand.Execute(Model).Subscribe();
    }

    /// <summary>
    /// Adds the peer without its user data, which the list requests for all peers at once and
    /// attaches with <see cref="UpdateUserModel"/>.
    /// </summary>
    public void AddPeerToSession(PeerModel peerModel)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == peerModel.UserId);
        if (peer is { IsLeaving: false })
            return;

        // Came back while the old row was still folding away; the new one replaces it at once
        if (peer != null)
            Peers.Remove(peer);

        var added = new PeerVM(peerModel, peerModel.UserId == _currentUserId, _playbackService,
            _viewProfileCommand, ToggleScreenWatch, KickPeer, AdmitPeer)
        {
            IsInEnteredSession = IsEntered,
            IsWatchingScreen = _screenWatch.IsWatching(Model.Id, peerModel.UserId)
        };
        ApplyAccess(added);
        Peers.Add(added);
    }

    /// <summary>
    /// Shows what the current user may do in the session and to its peers, after their roles and whether they
    /// host it. Called again whenever the roles or the session change.
    /// </summary>
    /// <summary>The roles of the current user let it see the session. The server sends no session it may not see.</summary>
    public bool CanView
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>The roles of the current user let it join the session.</summary>
    public bool CanJoin
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>The roles of the current user let it change what roles may do in the session.</summary>
    public bool CanEditPermissions
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public void ApplyAccess(CoordinatorPermissions permissions)
    {
        _permissions = permissions;
        IsHost = Model is HybridSessionModel { HostId: var hostId } && hostId == _currentUserId;
        CanManage = permissions.CanInSession(Permission.ManageSessions, IsHost);
        CanArrange = permissions.Has(Permission.ManageSessions);
        CanView = permissions.HasIn(Model, Permission.ViewSessions);
        CanJoin = permissions.HasIn(Model, Permission.JoinSessions);
        CanEditPermissions = permissions.Has(Permission.ManageSessions | Permission.ManageRoles);

        foreach (var peer in Peers)
            ApplyAccess(peer);
    }

    private void ApplyAccess(PeerVM peer)
    {
        peer.CanKick = !peer.IsCurrentUser
                       && _permissions.CanInSessionOn(Permission.KickMembers, IsHost, peer.UserData?.RoleIds ?? []);
        peer.CanApprove = !peer.IsCurrentUser && peer.IsWaiting
                          && _permissions.CanInSession(Permission.ApproveMembers, IsHost);
    }

    private void AdmitPeer(PeerVM peer, bool isApproved)
    {
        if (!peer.IsCurrentUser)
            _admitPeerCommand.Execute((Model.Id, peer.PeerData.UserId, isApproved)).Subscribe();
    }

    private void KickPeer(PeerVM peer)
    {
        if (!peer.IsCurrentUser)
            _kickPeerCommand.Execute((Model.Id, peer.PeerData.UserId)).Subscribe();
    }

    /// <summary>
    /// Asks the session we are in to watch the screen of the peer or to stop, and shows the session
    /// when the screen is about to appear there.
    /// </summary>
    private void ToggleScreenWatch(PeerVM peer)
    {
        var isWatching = !peer.IsWatchingScreen;

        _screenWatch.Request(Model.Id, peer.PeerData.UserId, isWatching);

        if (isWatching)
            Connect();
    }
    
    public async Task RemovePeerFromSession(int userId)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId && !p.IsLeaving);
        if (peer == null) 
            return;

        if (SelectedPeer == peer)
            SelectedPeer = null;

        // The row folds away first, so the rows below slide up instead of jumping
        peer.IsLeaving = true;
        await Task.Delay(Presence.LeaveTime);

        Peers.Remove(peer);
    }

    /// <summary>
    /// Attaches the user data to the peer of that user. Matched by user id, since the server reuses
    /// peer ids once a peer leaves.
    /// </summary>
    public void UpdateUserModel(int userId, UserModel? userModel)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);
        if (peer == null)
            return;

        peer.UpdateUserModel(userModel);
        // Their roles may have changed with them
        ApplyAccess(peer);
    }
    
    public void ApplyPlayback(int userId, PeerPlayback playback)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);

        peer?.ApplyPlayback(playback);
    }

    public void UpdateSpeaking(int userId, bool isSpeaking)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);

        if (peer != null)
            peer.IsSpeaking = isSpeaking;
    }

    public void UpdatePeerStatus(int userId, HybridSessionPeerState state)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);
        if (peer == null)
            return;

        peer.UpdatePeerStatus(state);
        // Being let in ends the decision
        ApplyAccess(peer);
    }

    public void UpdatePeerMicrophone(int userId, bool isMuted)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);

        peer?.UpdateMicrophoneMuted(isMuted);
    }

    public void UpdatePeerSound(int userId, bool isMuted)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);

        peer?.UpdateSoundMuted(isMuted);
    }

    public void UpdateScreenWatch(int userId, bool isWatching)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);

        if (peer != null)
            peer.IsWatchingScreen = isWatching;
    }

    public void UpdatePeerScreenShare(int userId, bool isActive)
    {
        var peer = Peers.FirstOrDefault(p => p.PeerData.UserId == userId);

        peer?.UpdateScreenShare(isActive);
    }
}