using Client.Application.Ports;


namespace Client.Api;

internal partial class CoordinatorGateway
{
    public async Task<TransportConnectResult> ConnectWebSocketAsync(string address, int port, string? trustedFingerprint,
        CancellationToken cancellationToken = default)
    {
        await ReleaseWebSocketTokenAsync();
        _webSocketTokenSource = new CancellationTokenSource();

        TransportConnectResult result;

        try
        {
            result = await _webSocketGateway.ConnectAsync(address, port, trustedFingerprint, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await ReleaseWebSocketTokenAsync();
            throw;
        }

        if (result.IsSuccess)
        {
            Interlocked.Exchange(ref _isConnectionLossReported, 0);
            OpenMedia(address, port, trustedFingerprint);

            _ = _heartbeatHandler.StartAsync(SendPingAsync, _webSocketTokenSource.Token);
        }
        else
            await ReleaseWebSocketTokenAsync();

        return result;
    }

    public async Task DisconnectWebSocketAsync()
    {
        // Closed on purpose, so it is not reported as a lost connection
        Interlocked.Exchange(ref _isConnectionLossReported, 1);

        await ReleaseWebSocketTokenAsync();

        await _webSocketGateway.DisconnectAsync();
        _responseAwaiter.FailAll(new ConnectionLostException("Disconnected from the coordinator"));
    }

    public async Task<bool> CheckConnectionAsync(TimeSpan timeout)
    {
        try
        {
            await SendPingAsync().WaitAsync(timeout);
            return true;
        }
        catch (RequestRejectedException)
        {
            // Refused by the rate limit, but an answer still proves the server is alive
            return true;
        }
        catch (TimeoutException)
        {
            // No answer in time, or the connection broke while waiting
            return false;
        }
    }

    private async Task ReleaseWebSocketTokenAsync()
    {
        if (_webSocketTokenSource != null)
        {
            try
            {
                await _webSocketTokenSource.CancelAsync();
            }
            catch (ObjectDisposedException) { }

            _webSocketTokenSource.Dispose();
            _webSocketTokenSource = null;
        }
    }
}
