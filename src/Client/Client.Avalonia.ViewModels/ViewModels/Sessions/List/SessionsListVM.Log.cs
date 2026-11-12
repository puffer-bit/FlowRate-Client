using Client.Core.Coordinator;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Sessions.List;

public partial class SessionsListVM
{
    [LoggerMessage(LogLevel.Error, "Failed to get the peers of hybrid session {SessionId} ({SessionName}): {Result}")]
    private partial void LogPeersRequestFailed(int sessionId, string sessionName, RequestResult result);

    [LoggerMessage(LogLevel.Warning, "Failed to load chat history for local unread counts: {Result}")]
    private partial void LogReadStatesLoadFailed(RequestResult result);
}
