using Client.Api.Mappers;
using Client.Api.Protocol;
using Client.Api.Transport;
using Client.Core.Events;
using Shared.Models;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Api;

internal partial class CoordinatorGateway : ICoordinatorGateway
{
    public event Action<CoordinatorEvent>? EventReceived;
    public event Action<string>? ConnectionLost;

    // 1 once the loss of the current connection is reported, so the socket and the heartbeat
    // noticing it both do not report it twice
    private int _isConnectionLossReported = 1;

    private readonly ResponseAwaiter _responseAwaiter;
    private readonly IHeartbeatHandler _heartbeatHandler;
    private readonly ILogger<CoordinatorGateway> _logger;

    private readonly IWebSocketGateway _webSocketGateway;
    private CancellationTokenSource? _webSocketTokenSource;

    public CoordinatorGateway(
        IHeartbeatHandler heartbeatHandler,
        ResponseAwaiter responseAwaiter,
        IWebSocketGateway webSocketGateway,
        ILogger<CoordinatorGateway> logger)
    {
        _responseAwaiter = responseAwaiter;
        _heartbeatHandler = heartbeatHandler;
        _logger = logger;

        _webSocketGateway = webSocketGateway;

        _webSocketGateway.MessageReceived += ProcessMessage;
        _webSocketGateway.ConnectionLost += ReportConnectionLost;
        _heartbeatHandler.Timeout += TimeoutSpotted;
        _heartbeatHandler.RoundTripMeasured += ReportRoundTrip;
    }

    private void ReportRoundTrip(TimeSpan roundTrip) =>
        EventReceived?.Invoke(new ServerRoundTripMeasuredEvent(roundTrip));

    private void TimeoutSpotted() =>
        ReportConnectionLost("The server stopped answering heartbeats");

    private void ReportConnectionLost(string reason)
    {
        if (Interlocked.Exchange(ref _isConnectionLossReported, 1) == 1)
            return;

        _responseAwaiter.FailAll(new ConnectionLostException($"Connection lost: {reason}"));
        LogConnectionLost(reason);

        ConnectionLost?.Invoke(reason);
    }

    private void ProcessMessage(BaseMessage baseMessage)
    {
        if (_responseAwaiter.HandleIncoming(baseMessage))
            return;

        var coordinatorEvent = baseMessage.ToEvent();

        if (coordinatorEvent == null)
        {
            // Expected from the server, so throwing here would take the connection down on ordinary events
            LogUnhandledMessage(baseMessage.GetType().Name);
            return;
        }

        EventReceived?.Invoke(coordinatorEvent);
    }

    public void Dispose()
    {
        _webSocketGateway.MessageReceived -= ProcessMessage;
        _webSocketGateway.ConnectionLost -= ReportConnectionLost;
        _heartbeatHandler.Timeout -= TimeoutSpotted;
        _heartbeatHandler.RoundTripMeasured -= ReportRoundTrip;

        _webSocketTokenSource?.Dispose();
        _webSocketGateway.Dispose();
        CloseMedia();

        EventReceived = null;
        ConnectionLost = null;
    }
}
