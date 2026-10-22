using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using ReactiveUI.Primitives;
using Shared.Enums;
using Client.Core.Coordinator;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;

namespace Client.ViewModels.Sessions.Hybrid;

public class HybridSessionParticipantsListVM : ReactiveObject
{
    // UI Collections
    public ObservableCollection<HybridSessionParticipantVM> Participants { get; set; } = new();
    
    // UI Commands
    public ReactiveCommand<HybridSessionParticipantVM, RxVoid> ApproveParticipant { get; }
    public ReactiveCommand<HybridSessionParticipantVM, RxVoid> RejectParticipant { get; }
    public ReactiveCommand<HybridSessionParticipantVM, RxVoid> KickParticipant { get; }

    private readonly int _currentUserId;

    // What the current user may do and whether they host the session, as of the last ApplyAccess
    private CoordinatorPermissions _permissions = CoordinatorPermissions.None;
    private bool _isHost;

    /// <param name="kickPeer">Removes the user with the given id from the session.</param>
    /// <param name="approvePeer">Lets the user with the given id, who waits for it, into the session.</param>
    /// <param name="rejectPeer">Turns away the user with the given id, who waits to be let in.</param>
    public HybridSessionParticipantsListVM(int currentUserId, Action<int> kickPeer, Action<int> approvePeer,
        Action<int> rejectPeer)
    {
        _currentUserId = currentUserId;
        KickParticipant = ReactiveCommand.Create<HybridSessionParticipantVM>(participant =>
        {
            if (participant.CanKick)
                kickPeer(participant.PeerData.UserId);
        });
        ApproveParticipant = ReactiveCommand.Create<HybridSessionParticipantVM>(participant =>
        {
            if (participant.CanApprove)
                approvePeer(participant.PeerData.UserId);
        });
        RejectParticipant = ReactiveCommand.Create<HybridSessionParticipantVM>(participant =>
        {
            if (participant.CanApprove)
                rejectPeer(participant.PeerData.UserId);
        });
    }

    public void AddPeerToSession(PeerModel peerModel)
    {
        var peer = Participants.FirstOrDefault(p => p.PeerData.UserId == peerModel.UserId);
        if (peer != null) 
            return;

        var added = new HybridSessionParticipantVM(peerModel, peerModel.UserId == _currentUserId);
        ApplyAccess(added);
        Participants.Add(added);
    }

    /// <summary>
    /// Shows what the current user may do to the participants, after their roles and whether they host the
    /// session. Called again whenever either changes.
    /// </summary>
    public void ApplyAccess(CoordinatorPermissions permissions, bool isHost)
    {
        _permissions = permissions;
        _isHost = isHost;

        foreach (var participant in Participants)
            ApplyAccess(participant);
    }

    private void ApplyAccess(HybridSessionParticipantVM participant)
    {
        participant.CanKick = !participant.IsCurrentUser
                              && _permissions.CanInSessionOn(Permission.KickMembers, _isHost,
                                  participant.UserData?.RoleIds ?? []);
        participant.CanApprove = !participant.IsCurrentUser && participant.IsWaiting
                                 && _permissions.CanInSession(Permission.ApproveMembers, _isHost);
    }
    
    public void RemovePeerFromSession(int userId)
    {
        var peer = Participants.FirstOrDefault(p => p.PeerData.UserId == userId);
        if (peer == null) 
            return;

        Participants.Remove(peer);
    }
    
    /// <summary>
    /// Attaches the user data to the peer of that user. Matched by user id, since the server reuses
    /// peer ids once a peer leaves.
    /// </summary>
    public void UpdateUserModel(int userId, UserModel? userModel)
    {
        var peer = Participants.FirstOrDefault(p => p.PeerData.UserId == userId);
        if (peer == null)
            return;

        peer.UpdateUserModel(userModel);
        // Their roles may have changed with them
        ApplyAccess(peer);
    }

    public void UpdateSpeaking(int userId, bool isSpeaking)
    {
        var peer = Participants.FirstOrDefault(p => p.PeerData.UserId == userId);

        if (peer != null)
            peer.IsSpeaking = isSpeaking;
    }

    public void UpdateScreenShare(int userId, bool isSharing)
    {
        var peer = Participants.FirstOrDefault(p => p.PeerData.UserId == userId);

        if (peer != null)
            peer.IsSharingScreen = isSharing;
    }

    public void UpdatePeerStatus(int userId, HybridSessionPeerState newState)
    {
        var peer = Participants.FirstOrDefault(p => p.PeerData.UserId == userId);

        if (peer == null)
            return;

        peer.UpdatePeerStatus(newState);
        // Being let in ends the decision
        ApplyAccess(peer);
    }
}