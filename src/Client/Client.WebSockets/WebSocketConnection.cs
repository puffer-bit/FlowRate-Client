using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Client.WebSockets;

/// <summary>
/// A WebSocket connection over <see cref="ClientWebSocket"/> with a bounded receive buffer
/// and a single send queue for text and binary messages. Each instance connects once; reconnecting means creating a new one.
/// Liveness checks and reconnects are left to the caller.
/// </summary>
/// <remarks>
/// Events are raised on the connection's own loops, not on the caller's thread. A
/// <see cref="MessageReceived"/> handler must not throw: an exception from it is treated as fatal and
/// ends the connection with <see cref="WebSocketCloseStatus.InternalServerError"/>.
/// </remarks>
public sealed class WebSocketConnection : IAsyncDisposable
{
    private const int ReceiveChunkSize = 16 * 1024;

    // A buffer grown by a large message is dropped afterwards instead of being kept for good
    private const int RetainedBufferLimit = 256 * 1024;

    /// <summary>A complete incoming message; its payload is only valid during the call.</summary>
    public event Action<WebSocketMessage>? MessageReceived;

    /// <summary>
    /// Raised once when the connection ends for any reason other than <see cref="CloseAsync"/>
    /// or <see cref="DisposeAsync"/> called on this side.
    /// </summary>
    public event Action<WebSocketDisconnectInfo>? Disconnected;

    public bool IsOpen => Volatile.Read(ref _finished) == 0 && _socket.State == WebSocketState.Open;

    /// <summary>
    /// Status of the server's HTTP answer to the upgrade request, kept also when <see cref="ConnectAsync"/>
    /// failed, so a refusal can be told apart from an unreachable server. Zero when there was no answer.
    /// </summary>
    public HttpStatusCode HandshakeStatusCode => _socket.HttpStatusCode;

    /// <summary>Headers of that answer; null when there was no answer.</summary>
    public IReadOnlyDictionary<string, IEnumerable<string>>? HandshakeResponseHeaders => _socket.HttpResponseHeaders;

    private readonly WebSocketConnectionOptions _options;
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Channel<OutgoingMessage> _sendQueue;

    private Task? _receiveLoop;
    private Task? _sendLoop;
    private int _connectStarted;
    private int _finished;

    public WebSocketConnection(WebSocketConnectionOptions? options = null)
    {
        _options = options ?? new WebSocketConnectionOptions();
        if (_options.MaxReceiveMessageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxReceiveMessageSize must be positive.");

        _sendQueue = Channel.CreateBounded<OutgoingMessage>(new BoundedChannelOptions(_options.MaxSendQueueLength)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <exception cref="TimeoutException">The server did not complete the handshake in time.</exception>
    /// <exception cref="WebSocketException">The connection could not be established.</exception>
    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _connectStarted, 1) != 0)
            throw new InvalidOperationException("A WebSocketConnection connects only once; create a new one to reconnect.");

        _socket.Options.KeepAliveInterval = TimeSpan.Zero;
        _socket.Options.CollectHttpResponseDetails = true;
        _options.Configure?.Invoke(_socket.Options);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        connectCts.CancelAfter(_options.ConnectTimeout);

        try
        {
            await _socket.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException($"No WebSocket handshake from {uri} within {_options.ConnectTimeout.TotalSeconds:0.#} s.");
        }

        _receiveLoop = Task.Run(ReceiveLoopAsync);
        _sendLoop = Task.Run(SendLoopAsync);
    }

    /// <summary>Queues a text message. Messages are sent one at a time in queue order.</summary>
    /// <returns>False when the connection is not open or the send queue is full.</returns>
    public bool SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsOpen)
            return false;

        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(text.Length));
        var length = Encoding.UTF8.GetBytes(text, buffer);
        return Enqueue(new OutgoingMessage(WebSocketMessageType.Text, buffer, length));
    }

    /// <summary>Queues a binary message; the payload is copied, so the caller may reuse its buffer.</summary>
    /// <returns>False when the connection is not open or the send queue is full.</returns>
    public bool SendBinary(ReadOnlySpan<byte> payload)
    {
        if (!IsOpen)
            return false;

        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(payload.Length, 1));
        payload.CopyTo(buffer);
        return Enqueue(new OutgoingMessage(WebSocketMessageType.Binary, buffer, payload.Length));
    }

    private bool Enqueue(OutgoingMessage message)
    {
        if (_sendQueue.Writer.TryWrite(message))
            return true;

        ArrayPool<byte>.Shared.Return(message.Buffer);
        return false;
    }

    /// <summary>
    /// Starts the close handshake and waits up to <see cref="WebSocketConnectionOptions.CloseTimeout"/>
    /// for the server to answer. Messages still queued are dropped. Does not raise <see cref="Disconnected"/>.
    /// </summary>
    public async Task CloseAsync(
        WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure,
        string? description = null)
    {
        if (!TryFinish())
            return;

        _sendQueue.Writer.TryComplete();

        if (_socket.State == WebSocketState.Open)
        {
            await SendCloseFrameAsync(status, description).ConfigureAwait(false);

            // The receive loop sees the server's close frame and ends; after the timeout the socket is aborted anyway
            if (_receiveLoop != null)
                await Task.WhenAny(_receiveLoop, Task.Delay(_options.CloseTimeout)).ConfigureAwait(false);
        }

        Teardown();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);

        var loops = new[] { _receiveLoop, _sendLoop }.OfType<Task>().ToArray();
        if (loops.Length > 0)
            await Task.WhenAll(loops).ConfigureAwait(false);

        _socket.Dispose();
        _lifetime.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        var token = _lifetime.Token;
        var chunk = ArrayPool<byte>.Shared.Rent(ReceiveChunkSize);
        var message = new ArrayBufferWriter<byte>(ReceiveChunkSize);

        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(chunk.AsMemory(), token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await OnCloseFrameReceivedAsync().ConfigureAwait(false);
                    return;
                }

                // Checked before copying, so an oversized message never grows the buffer past the limit
                if (message.WrittenCount + result.Count > _options.MaxReceiveMessageSize)
                {
                    await FailWithCloseAsync(WebSocketCloseStatus.MessageTooBig,
                        $"Incoming message exceeds {_options.MaxReceiveMessageSize} bytes").ConfigureAwait(false);
                    return;
                }

                message.Write(chunk.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                    continue;

                // ClientWebSocket validates UTF-8 in text messages itself: malformed text never gets
                // here, the socket closes with InvalidPayloadData and the receive above throws
                try
                {
                    MessageReceived?.Invoke(new WebSocketMessage(result.MessageType, message.WrittenMemory));
                }
                catch (Exception ex)
                {
                    await FailWithCloseAsync(WebSocketCloseStatus.InternalServerError,
                        $"Message handler failed: {ex.Message}", ex).ConfigureAwait(false);
                    return;
                }

                if (message.Capacity > RetainedBufferLimit)
                    message = new ArrayBufferWriter<byte>(ReceiveChunkSize);
                else
                    message.ResetWrittenCount();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Fail(new WebSocketDisconnectInfo($"Connection broken: {ex.Message}", null, ex));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private async Task SendLoopAsync()
    {
        var token = _lifetime.Token;

        try
        {
            await foreach (var outgoing in _sendQueue.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await _sendLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await _socket.SendAsync(outgoing.Buffer.AsMemory(0, outgoing.Length), outgoing.Type,
                            endOfMessage: true, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(outgoing.Buffer);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Fail(new WebSocketDisconnectInfo($"Send failed: {ex.Message}", null, ex));
        }
    }

    private async Task OnCloseFrameReceivedAsync()
    {
        // Our own close is being answered: CloseAsync finishes the teardown
        if (!TryFinish())
            return;

        var status = _socket.CloseStatus;
        var description = _socket.CloseStatusDescription;

        _sendQueue.Writer.TryComplete();
        if (_socket.State == WebSocketState.CloseReceived)
            await SendCloseFrameAsync(status ?? WebSocketCloseStatus.NormalClosure, null).ConfigureAwait(false);
        Teardown();

        var reason = string.IsNullOrWhiteSpace(description)
            ? $"Closed by server ({status?.ToString() ?? "no status"})"
            : $"Closed by server: {description}";
        Disconnected?.Invoke(new WebSocketDisconnectInfo(reason, status));
    }

    private async Task FailWithCloseAsync(WebSocketCloseStatus status, string reason, Exception? exception = null)
    {
        if (!TryFinish())
            return;

        _sendQueue.Writer.TryComplete();
        await SendCloseFrameAsync(status, reason).ConfigureAwait(false);
        Teardown();

        Disconnected?.Invoke(new WebSocketDisconnectInfo(reason, status, exception));
    }

    private void Fail(WebSocketDisconnectInfo info)
    {
        if (!TryFinish())
            return;

        _sendQueue.Writer.TryComplete();
        Teardown();

        Disconnected?.Invoke(info);
    }

    /// <summary>Sends a close frame without waiting for the answer; failures only mean the peer is already gone.</summary>
    private async Task SendCloseFrameAsync(WebSocketCloseStatus status, string? description)
    {
        using var timeout = new CancellationTokenSource(_options.CloseTimeout);
        try
        {
            await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await _socket.CloseOutputAsync(status, Truncate(description), timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
        }
    }

    private bool TryFinish() => Interlocked.Exchange(ref _finished, 1) == 0;

    private void Teardown()
    {
        _lifetime.Cancel();
        _socket.Abort();
    }

    // A close frame carries at most 123 bytes of UTF-8 description
    private static string? Truncate(string? description)
    {
        if (description == null || Encoding.UTF8.GetByteCount(description) <= 123)
            return description;

        var length = description.Length;
        while (length > 0 && Encoding.UTF8.GetByteCount(description.AsSpan(0, length)) > 123)
            length--;
        return description[..length];
    }

    // Buffer is rented from ArrayPool and returned once sent; messages dropped at close are left to the GC
    private readonly record struct OutgoingMessage(WebSocketMessageType Type, byte[] Buffer, int Length);
}
