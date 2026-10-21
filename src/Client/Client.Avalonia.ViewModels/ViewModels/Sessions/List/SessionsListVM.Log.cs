using Microsoft.Extensions.Logging;
using Shared.Enums;

namespace Client.ViewModels.Sessions.List;

public partial class SessionsListVM
{
    [LoggerMessage(LogLevel.Error, "Failed to get the peers of hybrid session {SessionId} ({SessionName}): {Result}")]
    private partial void LogPeersRequestFailed(int sessionId, string sessionName, PeerListRequestResult result);

    [LoggerMessage(LogLevel.Warning, "Failed to get the unread counts of the chats: {Result}")]
    private partial void LogReadStatesLoadFailed(ChatReadResult result);
}
