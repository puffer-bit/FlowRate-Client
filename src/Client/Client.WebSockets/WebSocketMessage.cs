using System.Net.WebSockets;
using System.Text;

namespace Client.WebSockets;

/// <summary>A complete incoming message, text or binary.</summary>
public readonly struct WebSocketMessage
{
    public WebSocketMessageType Type { get; }

    /// <summary>
    /// Valid only while the event handler runs: the buffer is reused for the next message.
    /// Copy it to keep it.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    internal WebSocketMessage(WebSocketMessageType type, ReadOnlyMemory<byte> payload)
    {
        Type = type;
        Payload = payload;
    }

    /// <summary>
    /// The payload as UTF-8 text. For text messages <see cref="ClientWebSocket"/> has already
    /// rejected malformed UTF-8.
    /// </summary>
    public string GetText() => Encoding.UTF8.GetString(Payload.Span);
}
