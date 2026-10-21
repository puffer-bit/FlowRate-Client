using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Client.WebSockets.Tests;

public class WebSocketConnectionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Messages_AreSentAndReceivedInOrder()
    {
        await using var server = await TestWebSocketServer.StartAsync((socket, s) =>
            s.ReceiveUntilClosedAsync(socket, text =>
                socket.SendAsync(Encoding.UTF8.GetBytes("echo:" + text), WebSocketMessageType.Text, true, CancellationToken.None)));

        await using var connection = new WebSocketConnection();
        var received = new List<string>();
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.MessageReceived += message =>
        {
            lock (received)
            {
                received.Add(message.GetText());
                if (received.Count == 100)
                    allReceived.TrySetResult();
            }
        };

        await connection.ConnectAsync(server.Uri);
        for (var i = 0; i < 100; i++)
            Assert.True(connection.SendText($"message {i}"));

        await allReceived.Task.WaitAsync(Timeout);
        Assert.Equal(Enumerable.Range(0, 100).Select(i => $"echo:message {i}"), received);
    }

    [Fact]
    public async Task FragmentedMessage_UnderLimit_IsAssembled()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, s) =>
        {
            var part = Encoding.UTF8.GetBytes(new string('a', 40_000));
            await socket.SendAsync(part, WebSocketMessageType.Text, false, CancellationToken.None);
            await socket.SendAsync(part, WebSocketMessageType.Text, true, CancellationToken.None);
            await s.ReceiveUntilClosedAsync(socket);
        });

        await using var connection = new WebSocketConnection(new WebSocketConnectionOptions { MaxReceiveMessageSize = 100_000 });
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.MessageReceived += message => received.TrySetResult(message.GetText());

        await connection.ConnectAsync(server.Uri);

        Assert.Equal(80_000, (await received.Task.WaitAsync(Timeout)).Length);
    }

    [Fact]
    public async Task OversizedMessage_ClosesWithMessageTooBig()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, s) =>
        {
            await socket.SendAsync(new byte[2048], WebSocketMessageType.Text, true, CancellationToken.None);
            await s.ReceiveUntilClosedAsync(socket);
        });

        await using var connection = new WebSocketConnection(new WebSocketConnectionOptions { MaxReceiveMessageSize = 1024 });
        var textReceived = false;
        connection.MessageReceived += _ => textReceived = true;
        var disconnected = DisconnectedTask(connection);

        await connection.ConnectAsync(server.Uri);

        var info = await disconnected.WaitAsync(Timeout);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, info.CloseStatus);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, await server.ClientCloseStatus.WaitAsync(Timeout));
        Assert.False(textReceived);
        Assert.False(connection.IsOpen);
    }

    [Fact]
    public async Task BinaryMessages_RoundTripWithTheirType()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, s) =>
        {
            var buffer = new byte[1024];
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            await socket.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, true, CancellationToken.None);
            await s.ReceiveUntilClosedAsync(socket);
        });

        await using var connection = new WebSocketConnection();
        var received = new TaskCompletionSource<(WebSocketMessageType Type, byte[] Payload)>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.MessageReceived += message => received.TrySetResult((message.Type, message.Payload.ToArray()));

        await connection.ConnectAsync(server.Uri);
        var payload = new byte[] { 0, 1, 2, 0xFF };
        Assert.True(connection.SendBinary(payload));

        var (type, echoed) = await received.Task.WaitAsync(Timeout);
        Assert.Equal(WebSocketMessageType.Binary, type);
        Assert.Equal(payload, echoed);
    }

    [Fact]
    public async Task InvalidUtf8_ClosesWithInvalidPayloadData()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, s) =>
        {
            // Kestrel does not validate outgoing text frames, so malformed UTF-8 reaches the client as is
            await socket.SendAsync(new byte[] { 0xC3, 0x28 }, WebSocketMessageType.Text, true, CancellationToken.None);
            await s.ReceiveUntilClosedAsync(socket);
        });

        await using var connection = new WebSocketConnection();
        var textReceived = false;
        connection.MessageReceived += _ => textReceived = true;
        var disconnected = DisconnectedTask(connection);

        await connection.ConnectAsync(server.Uri);

        await disconnected.WaitAsync(Timeout);
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, await server.ClientCloseStatus.WaitAsync(Timeout));
        Assert.False(textReceived);
    }

    [Fact]
    public async Task ServerClose_RaisesDisconnectedWithItsDescription()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, _) =>
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth timeout", CancellationToken.None);
        });

        await using var connection = new WebSocketConnection();
        var disconnected = DisconnectedTask(connection);

        await connection.ConnectAsync(server.Uri);

        var info = await disconnected.WaitAsync(Timeout);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, info.CloseStatus);
        Assert.Contains("auth timeout", info.Reason);
    }

    [Fact]
    public async Task AbortedConnection_RaisesDisconnected()
    {
        await using var server = await TestWebSocketServer.StartAsync((socket, _) =>
        {
            socket.Abort();
            return Task.CompletedTask;
        });

        await using var connection = new WebSocketConnection();
        var disconnected = DisconnectedTask(connection);

        await connection.ConnectAsync(server.Uri);

        var info = await disconnected.WaitAsync(Timeout);
        Assert.Null(info.CloseStatus);
    }

    [Fact]
    public async Task LocalClose_SendsNormalClosure_AndDoesNotRaiseDisconnected()
    {
        await using var server = await TestWebSocketServer.StartAsync((socket, s) => s.ReceiveUntilClosedAsync(socket));

        var connection = new WebSocketConnection();
        var raised = false;
        connection.Disconnected += _ => raised = true;

        await connection.ConnectAsync(server.Uri);
        await connection.CloseAsync();
        await connection.DisposeAsync();

        Assert.Equal(WebSocketCloseStatus.NormalClosure, await server.ClientCloseStatus.WaitAsync(Timeout));
        Assert.False(raised);
        Assert.False(connection.SendText("late"));
    }

    [Fact]
    public async Task ThrowingHandler_ClosesWithInternalServerError()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, s) =>
        {
            await socket.SendAsync("boom"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
            await s.ReceiveUntilClosedAsync(socket);
        });

        await using var connection = new WebSocketConnection();
        connection.MessageReceived += _ => throw new InvalidOperationException("handler bug");
        var disconnected = DisconnectedTask(connection);

        await connection.ConnectAsync(server.Uri);

        var info = await disconnected.WaitAsync(Timeout);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, info.CloseStatus);
        Assert.IsType<InvalidOperationException>(info.Exception);
    }

    [Fact]
    public async Task Connect_WithoutHandshake_TimesOut()
    {
        // Accepts TCP but never answers the HTTP upgrade
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await using var connection = new WebSocketConnection(new WebSocketConnectionOptions
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(300)
        });

        await Assert.ThrowsAsync<TimeoutException>(() => connection.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/")));
        Assert.False(connection.IsOpen);
    }

    [Fact]
    public async Task Send_BeforeConnect_ReturnsFalse()
    {
        await using var connection = new WebSocketConnection();

        Assert.False(connection.SendText("nobody listens"));
    }

    [Fact]
    public async Task Connect_Twice_Throws()
    {
        await using var server = await TestWebSocketServer.StartAsync((socket, s) => s.ReceiveUntilClosedAsync(socket));
        await using var connection = new WebSocketConnection();

        await connection.ConnectAsync(server.Uri);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ConnectAsync(server.Uri));
    }

    [Fact]
    public async Task RefusedHandshake_KeepsTheStatusAndHeadersOfTheAnswer()
    {
        string? requestHeader = null;
        await using var server = await TestWebSocketServer.StartAsync((socket, s) => s.ReceiveUntilClosedAsync(socket),
            context =>
            {
                requestHeader = context.Request.Headers["Test-Version"];
                context.Response.Headers["Test-Version"] = "7";
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                return false;
            });
        await using var connection = new WebSocketConnection(new WebSocketConnectionOptions
        {
            Configure = options => options.SetRequestHeader("Test-Version", "3")
        });

        await Assert.ThrowsAsync<WebSocketException>(() => connection.ConnectAsync(server.Uri, TestContext.Current.CancellationToken));

        Assert.Equal("3", requestHeader);
        Assert.Equal(HttpStatusCode.UpgradeRequired, connection.HandshakeStatusCode);
        Assert.Equal("7", connection.HandshakeResponseHeaders!["Test-Version"].Single());
    }

    private static Task<WebSocketDisconnectInfo> DisconnectedTask(WebSocketConnection connection)
    {
        var tcs = new TaskCompletionSource<WebSocketDisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Disconnected += info => tcs.TrySetResult(info);
        return tcs.Task;
    }
}
