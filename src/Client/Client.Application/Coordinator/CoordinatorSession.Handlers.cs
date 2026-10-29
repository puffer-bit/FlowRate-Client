using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;

namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    private void RaiseSessionAdded(SessionModel session)
    {
        switch (session)
        {
            case HybridSessionModel hybrid:
                HybridSessionAdded?.Invoke(hybrid);
                LogSessionAdded(LogName, "Hybrid", hybrid.Id, hybrid.Name);
                break;

            case ChatSessionModel chat:
                ChatSessionAdded?.Invoke(chat);
                LogSessionAdded(LogName, "Chat", chat.Id, chat.Name);
                break;
        }
    }
    
    private void RaiseSessionReconfigured(SessionModel session)
    {
        switch (session)
        {
            case HybridSessionModel hybrid:
                HybridSessionReconfigured?.Invoke(hybrid);
                LogSessionReconfigured(LogName, "Hybrid", hybrid.Id, hybrid.Name);
                break;

            case ChatSessionModel chat:
                ChatSessionReconfigured?.Invoke(chat);
                LogSessionReconfigured(LogName, "Chat", chat.Id, chat.Name);
                break;
        }
    }
    
    private void RaiseSessionDeleted(SessionDeletedEvent deleted)
    {
        switch (deleted.SessionType)
        {
            case Shared.Enums.SessionType.Hybrid:
                HybridSessionDeleted?.Invoke(deleted.SessionId);
                LogSessionDeleted(LogName, "Hybrid", deleted.SessionId);
                break;

            case Shared.Enums.SessionType.Chat:
                ChatSessionDeleted?.Invoke(deleted.SessionId);
                LogSessionDeleted(LogName, "Chat", deleted.SessionId);
                break;
        }
    }
}
