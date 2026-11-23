using System;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Main;

public partial class MainWindowVM
{
    [LoggerMessage(LogLevel.Warning, "[{Coordinator}] Failed to remember the successful connection")]
    private partial void LogSuccessfulConnectionSaveFailed(Exception exception, string coordinator);

    [LoggerMessage(LogLevel.Error, "[{Coordinator}] Failed to retrieve the sessions info")]
    private partial void LogSessionsInfoFailed(Exception exception, string coordinator);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Loading the sessions info stopped: the connection is gone")]
    private partial void LogSessionsLoadInterrupted(Exception exception, string coordinator);

    [LoggerMessage(LogLevel.Information, "[{Coordinator}] Auto connection attempt {Attempt} failed with {State}, trying again in {Delay}")]
    private partial void LogAutoConnectRetry(string coordinator, int attempt, Client.Core.Coordinator.CoordinatorState state, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "[{Coordinator}] Auto connection attempt {Attempt} failed")]
    private partial void LogAutoConnectAttemptFailed(Exception exception, string coordinator, int attempt);
}
