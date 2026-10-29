using Client.Core.Coordinator;


namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    private void ProcessEvent(CoordinatorEvent coordinatorEvent)
    {
        switch (coordinatorEvent)
        {
            case ServerClosingEvent:
                _ = Disconnect();
                break;

            // The identity would log in again at once: the device stays out until the user connects
            case SessionEndedEvent ended:
                LogSessionEnded(LogName, ended.Reason);
                KeepSessionToken(null);
                _ = Disconnect();
                SessionEnded?.Invoke(ended.Reason);
                break;

            case CoordinatorReconfiguredEvent reconfigured:
                CoordinatorModel = reconfigured.Coordinator;
                CoordinatorReconfigured?.Invoke(reconfigured.Coordinator);
                break;

            case OnlineUsersChangedEvent onlineChanged:
                if (CoordinatorModel != null)
                    CoordinatorModel.ConnectedUsersCount = onlineChanged.ConnectedUsersCount;
                OnlineUsersChanged?.Invoke(onlineChanged.ConnectedUsersCount);
                break;

            case ServerRoundTripMeasuredEvent measured:
                ServerRoundTrip = measured.RoundTrip;
                break;

            case ServerRestartingEvent restarting:
                OnConnectionLost($"The server is restarting. {restarting.Message}");
                break;
            
            case RolesChangedEvent rolesChanged:
                OnRolesChanged(rolesChanged.Roles);
                break;

            case UserUpdatedEvent userUpdated:
                OnUserUpdated(userUpdated.User);
                break;

            case UserDeletedEvent userDeleted:
                OnUserDeleted(userDeleted.UserId);
                break;

            case SessionAddedEvent added:
                RaiseSessionAdded(added.Session);
                break;

            case SessionReconfiguredEvent reconfigured:
                RaiseSessionReconfigured(reconfigured.Session);
                break;

            case SessionDeletedEvent deleted:
                RaiseSessionDeleted(deleted);
                break;

            case SessionLayoutChangedEvent layoutChanged:
                SessionLayoutChanged?.Invoke(layoutChanged.Layout);
                break;
            
            case HybridSessionPeerEvent peerEvent:
                RaisePeerEvent(peerEvent);
                break;

            case HybridSessionNodeEvent nodeEvent:
                HybridSessionNodeChanged?.Invoke(nodeEvent);
                break;
            
            case ChatMessageAddedEvent messageAdded:
                messageAdded.Message.IsMine = messageAdded.Message.SenderUserId == GetCurrentUser().Id;
                TextMessageAdded?.Invoke(messageAdded.ChatId, messageAdded.Message);
                break;

            case ChatMessageEditedEvent messageEdited:
                messageEdited.Message.IsMine = messageEdited.Message.SenderUserId == GetCurrentUser().Id;
                TextMessageEdited?.Invoke(messageEdited.ChatId, messageEdited.Message);
                break;

            case ChatMessageDeletedEvent messageDeleted:
                TextMessageDeleted?.Invoke(messageDeleted.ChatId, messageDeleted.MessageId, messageDeleted.Revision);
                break;

            case ChatReadStateChangedEvent readChanged:
                ChatReadStateChanged?.Invoke(readChanged.State);
                break;
        }
    }

    private void RaisePeerEvent(HybridSessionPeerEvent peerEvent)
    {
        switch (peerEvent)
        {
            case PeerJoinedEvent joined:
                HybridSessionPeerJoined?.Invoke(joined);
                break;

            case PeerLeftEvent left:
                HybridSessionPeerLeft?.Invoke(left);
                break;

            case PeerStateChangedEvent stateChanged:
                HybridSessionPeerStateChanged?.Invoke(stateChanged);
                break;

            case PeerMicrophoneMuteChangedEvent muteChanged:
                HybridSessionPeerMicrophoneMuteChanged?.Invoke(muteChanged);
                break;

            case PeerSoundMuteChangedEvent muteChanged:
                HybridSessionPeerSoundMuteChanged?.Invoke(muteChanged);
                break;

            case PeerAfkChangedEvent afkChanged:
                HybridSessionPeerAfkChanged?.Invoke(afkChanged);
                break;

            case PeerPokedEvent poked:
                HybridSessionPeerPoked?.Invoke(poked);
                break;
        }

        HybridSessionUpdated?.Invoke(peerEvent);
    }
}
