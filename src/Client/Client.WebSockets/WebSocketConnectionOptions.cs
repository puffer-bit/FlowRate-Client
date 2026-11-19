using System.Net.WebSockets;

namespace Client.WebSockets;

public sealed class WebSocketConnectionOptions
{
    /// <summary>
    /// Largest incoming message in bytes. A bigger message closes the connection with
    /// <see cref="WebSocketCloseStatus.MessageTooBig"/> before more of it is buffered.
    /// </summary>
    public int MaxReceiveMessageSize { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Messages waiting to be sent. <see cref="WebSocketConnection.SendText(string)"/> refuses new ones once
    /// the queue is full, so a stalled connection cannot grow memory without bound.
    /// </summary>
    public int MaxSendQueueLength { get; init; } = 1024;

    /// <summary>Maximum pooled buffer bytes held by queued and in-flight sends.</summary>
    public long MaxSendQueueBytes { get; init; } = 8 * 1024 * 1024;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a local close waits for the server to answer the close handshake.</summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Applied to the socket options before connecting, e.g. for certificate validation.
    /// The protocol-level keepalive is off by default: liveness is the application heartbeat's job.
    /// </summary>
    public Action<ClientWebSocketOptions>? Configure { get; init; }
}
