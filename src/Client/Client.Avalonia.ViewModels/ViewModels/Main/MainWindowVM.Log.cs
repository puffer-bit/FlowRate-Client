using System;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Main;

public partial class MainWindowVM
{
    [LoggerMessage(LogLevel.Warning, "[{Coordinator}] Failed to keep the account password")]
    private partial void LogAccountPasswordSaveFailed(Exception exception, string coordinator);

    [LoggerMessage(LogLevel.Error, "[{Coordinator}] Failed to retrieve the sessions info")]
    private partial void LogSessionsInfoFailed(Exception exception, string coordinator);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Loading the sessions info stopped: the connection is gone")]
    private partial void LogSessionsLoadInterrupted(Exception exception, string coordinator);
}
