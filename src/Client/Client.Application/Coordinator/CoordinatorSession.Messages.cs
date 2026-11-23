using Client.Core.Coordinator;
using Shared.Enums;


namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    private void ProcessEvent(CoordinatorEvent coordinatorEvent)
    {
        switch (coordinatorEvent)
        {
            case ChatCallChangedEvent call:
                ApplyChatCall(call.Call, true);
                break;
            case BannedEvent banned:
                Post(new BanReceived(banned.Reason));
                break;

            // A server turned off is not waited for; any other goes away for a moment
            case ServerRestartEvent { At: null, Reason: ServerRestartReason.Shutdown } shutdown:
                ServerRestart?.Invoke(shutdown);
                _ = Disconnect();
                break;
            case ServerRestartEvent { At: null } restarting:
                ServerRestart?.Invoke(restarting);
                OnConnectionLost($"The server is going down ({restarting.Reason}). {restarting.Message}");
                break;
            case ServerRestartEvent warning:
                ServerRestart?.Invoke(warning);
                break;
            case ServerRestartCancelledEvent:
                ServerRestartCancelled?.Invoke();
                break;

            // The device is not one of the account any more: it stays out until the user logs in again
            case DeviceEndedEvent ended:
                LogDeviceEnded(LogName, ended.Reason);
                _ = Disconnect();
                OnDeviceEnded(ended.Reason);
                break;
            case DeviceAddedEvent added:
                DeviceAdded?.Invoke(added.Device);
                break;
            case DeviceLinkCompletedEvent or DeviceLinkDeclinedEvent:
                OnDeviceLinkEvent(coordinatorEvent);
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
                _chatTyping.Update(messageAdded.ChatId, messageAdded.Message.SenderUserId, false);
                TextMessageAdded?.Invoke(messageAdded.ChatId, messageAdded.Message);
                break;

            case ChatMessageEditedEvent messageEdited:
                messageEdited.Message.IsMine = messageEdited.Message.SenderUserId == GetCurrentUser().Id;
                // Reaction updates use this event too and must not stop the author's typing activity.
                TextMessageEdited?.Invoke(messageEdited.ChatId, messageEdited.Message);
                break;

            case ChatMessageDeletedEvent messageDeleted:
                TextMessageDeleted?.Invoke(messageDeleted.ChatId, messageDeleted.MessageId, messageDeleted.Revision);
                break;

            case ChatUserTypingEvent typing:
                if (ConnectionStatus != CoordinatorState.Connected || typing.UserId == GetCurrentUser().Id)
                    break;
                _chatTyping.Update(typing.ChatId, typing.UserId, typing.IsTyping);
                ChatUserTyping?.Invoke(typing.ChatId, typing.UserId, typing.IsTyping);
                break;

            case ChatViewersChangedEvent viewers:
                ChatViewersChanged?.Invoke(viewers);
                break;

            case ChatReadStateChangedEvent readChanged:
                ChatReadStateChanged?.Invoke(readChanged.State);
                break;

            case ChatPeerReadEvent peerRead:
                ChatPeerRead?.Invoke(peerRead.Read);
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
