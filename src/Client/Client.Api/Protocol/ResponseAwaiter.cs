using System.Collections.Concurrent;
using Shared.Models;
using Shared.Models.Responses;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Api.Protocol;

/// <summary>
/// Matches responses to the requests waiting for them. Requests are sent from any thread and
/// responses arrive on the socket thread.
/// </summary>
internal partial class ResponseAwaiter
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(40);

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<BaseMessage>> _pending = new();
    private readonly ILogger<ResponseAwaiter> _logger;

    public ResponseAwaiter(ILogger<ResponseAwaiter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers the request before <paramref name="send"/> runs, so a fast response cannot
    /// arrive before anyone waits for it.
    /// </summary>
    /// <exception cref="TimeoutException">No response in time, or the connection was lost.</exception>
    /// <exception cref="RequestFailedException">The server answered with an error.</exception>
    public async Task<BaseMessage> SendAndWaitAsync(Guid requestId, Action send)
    {
        var completion = new TaskCompletionSource<BaseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;

        try
        {
            send();

            return await completion.Task.WaitAsync(ResponseTimeout);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    /// <returns>True when the message is a response and needs no further handling.</returns>
    public bool HandleIncoming(BaseMessage message)
    {
        if (message is not Response response)
            return false;

        if (_pending.TryRemove(response.RequestId, out var completion))
        {
            if (response is ErrorResponse error)
            {
                LogRequestFailed(error.RequestId, error.Code, error.Details);
                completion.TrySetException(new RequestFailedException(error.Code, error.Details,
                    error.Fields?.AsReadOnly()));
            }
            else
            {
                completion.TrySetResult(response);
            }
        }
        else
            LogUnknownResponseDropped(response.RequestId);

        return true;
    }

    /// <summary>
    /// Fails every request still waiting, since their responses will never come.
    /// </summary>
    public void FailAll(Exception exception)
    {
        foreach (var requestId in _pending.Keys)
        {
            if (_pending.TryRemove(requestId, out var completion))
                completion.TrySetException(exception);
        }
    }
}
