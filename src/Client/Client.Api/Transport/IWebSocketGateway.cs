using Shared.Models;
using Client.Application.Ports;

namespace Client.Api.Transport;

internal interface IWebSocketGateway : IDisposable
{
    event Action<BaseMessage>? MessageReceived;

    /// <summary>
    /// Raised when an open connection breaks or the server closes it. Not raised when the
    /// connection is closed from this side.
    /// </summary>
    event Action<string>? ConnectionLost;
    
    /// <param name="trustedFingerprint">
    /// SHA-256 of the certificate the user trusted for this address. A certificate the system
    /// verifies is accepted without it.
    /// </param>
    /// <exception cref="OperationCanceledException">Canceled before the connection was open.</exception>
    Task<TransportConnectResult> ConnectAsync(
        string address, 
        int port,
        string? trustedFingerprint,
        CancellationToken cancellationToken = default);

    /// <returns>False when there is no open connection to send through.</returns>
    bool SendMessage(BaseMessage message);
    
    Task DisconnectAsync();
}