using System;
using System.Threading.Tasks;
using Client.Application.Ports.Repositories;
using Client.Core.Coordinator;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Main;

/// <summary>
/// Remembers what is on screen and the hybrid session we are in, per coordinator, to return
/// there once a lost connection is restored. The writes go one after another.
/// </summary>
public sealed partial class LastSessionKeeper(ICoordinatorStateRepository coordinatorStates, ILogger<LastSessionKeeper> logger)
{
    private readonly ILogger _logger = logger;

    // The writes of the last session, one after another
    private Task _write = Task.CompletedTask;

    /// <summary>
    /// Set from a lost connection until the sessions of that moment are entered again; what is
    /// remembered of them stays as it was meanwhile.
    /// </summary>
    public bool IsKept { get; set; }

    public void Remember(string coordinatorId, LastSession session)
    {
        if (!IsKept)
            QueueWrite(coordinatorId, session);
    }

    /// <summary>
    /// Left for good: there is nowhere to return to.
    /// </summary>
    public void Forget(string? coordinatorId)
    {
        IsKept = false;
        if (coordinatorId != null)
            QueueWrite(coordinatorId, null);
    }

    /// <summary>
    /// What was remembered for the coordinator, once the writes before have ended.
    /// </summary>
    public async Task<LastSession?> ReadAsync(string coordinatorId)
    {
        try
        {
            await _write;
            return await coordinatorStates.GetLastSessionAsync(coordinatorId);
        }
        catch (Exception exception)
        {
            LogRestoreFailed(exception);
            return null;
        }
    }

    private void QueueWrite(string coordinatorId, LastSession? session)
    {
        var previous = _write;

        _write = WriteAsync();

        async Task WriteAsync()
        {
            await previous;

            try
            {
                await coordinatorStates.SetLastSessionAsync(coordinatorId, session);
            }
            catch (Exception exception)
            {
                LogSaveFailed(exception);
            }
        }
    }

    [LoggerMessage(LogLevel.Warning, "Failed to save the last session")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(LogLevel.Warning, "Failed to read the last session")]
    private partial void LogRestoreFailed(Exception exception);
}
