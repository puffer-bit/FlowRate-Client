using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Client.WebSockets.Tests;

/// <summary>
/// A Kestrel server on a free local port that hands each accepted WebSocket to a test-defined
/// handler and records how the client closed it.
/// </summary>
internal sealed class TestWebSocketServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TaskCompletionSource<WebSocketCloseStatus?> _clientCloseStatus =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Uri Uri { get; }

    /// <summary>Close status the client sent, or null when the connection ended without a close frame.</summary>
    public Task<WebSocketCloseStatus?> ClientCloseStatus => _clientCloseStatus.Task;

    private TestWebSocketServer(WebApplication app, Uri uri)
    {
        _app = app;
        Uri = uri;
    }

    /// <param name="accept">Sees the upgrade request first; when it returns false, its response is the answer.</param>
    public static async Task<TestWebSocketServer> StartAsync(Func<WebSocket, TestWebSocketServer, Task> handler,
        Func<HttpContext, bool>? accept = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.Zero });

        TestWebSocketServer? server = null;
        app.Run(async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (accept != null && !accept(context))
                return;

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await handler(socket, server!);
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features
            .GetRequiredFeature<IServerAddressesFeature>().Addresses.First();

        server = new TestWebSocketServer(app, new Uri(address.Replace("http://", "ws://")));
        return server;
    }

    /// <summary>Reads until the client closes, answering text messages with <paramref name="onText"/>.</summary>
    public async Task ReceiveUntilClosedAsync(WebSocket socket, Func<string, Task>? onText = null)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _clientCloseStatus.TrySetResult(result.CloseStatus);
                    if (socket.State == WebSocketState.CloseReceived)
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    return;
                }

                if (onText != null)
                    await onText(System.Text.Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            _clientCloseStatus.TrySetResult(null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
