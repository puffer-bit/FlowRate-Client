# WebSocket transport

`WebSocketConnection` sends text and binary messages through one bounded queue.
`SendText(string)` encodes text; `SendText(ReadOnlySpan<byte>)` accepts already encoded
UTF-8 and still sends a WebSocket **text** message. The byte overload copies its input,
so callers may reuse it immediately. Callers must supply valid UTF-8.

The coordinator API serializes directly to UTF-8 with `ProtocolJsonContext` and parses
incoming UTF-8 spans. This preserves the JSON wire format and protocol version while
avoiding intermediate UTF-16 strings.

Complete messages that fit in a receive chunk are dispatched from the receive buffer.
An assembly buffer is allocated only when needed for fragmentation. Incoming payloads
are borrowed: `MessageReceived` handlers must finish parsing or copy the payload before
returning. The incoming size limit applies to the entire message, including fragments.

Outgoing messages are limited by both `MaxSendQueueLength` (default 1024 waiting
messages) and `MaxSendQueueBytes` (default 8 MiB). The byte limit counts the actual
pooled buffer capacities, including the send currently in progress. A buffer may be
larger than its payload due to pool bucket rounding. Sending returns `false` when a
limit is reached or the connection has closed; callers must handle this result.
Buffers are returned to the pool after sending, rejection, or shutdown.
