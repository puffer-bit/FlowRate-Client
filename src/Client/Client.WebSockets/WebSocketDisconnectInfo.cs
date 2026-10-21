using System.Net.WebSockets;

namespace Client.WebSockets;

/// <param name="Reason">Human-readable cause, suitable for logs and the UI.</param>
/// <param name="CloseStatus">Close code sent or received, null when the connection simply broke.</param>
/// <param name="Exception">The error that ended the connection, if any.</param>
public sealed record WebSocketDisconnectInfo(
    string Reason,
    WebSocketCloseStatus? CloseStatus = null,
    Exception? Exception = null);
