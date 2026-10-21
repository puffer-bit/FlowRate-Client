using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Client.Application.Ports;
using Client.Core.Push;
using Microsoft.Extensions.Logging;
using Shared.Models.Push;
using Shared.Serialization;

namespace Client.Api.Transport.HTTP;

internal sealed partial class PushReplyHttpTransport : IPushReplyTransport
{
    // The system gives a notification action about ten seconds
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly ILogger<PushReplyHttpTransport> _logger;

    public PushReplyHttpTransport(ILogger<PushReplyHttpTransport> logger)
    {
        _logger = logger;
    }

    public Task<PushReplyResult> SendAsync(PushReplyTarget target, string? trustedFingerprint, int chatId,
        string text, CancellationToken cancellationToken = default) =>
        PostAsync(target, trustedFingerprint, PushEndpoints.Reply,
            JsonContent.Create(new PushReplyRequest(chatId, text), ProtocolJsonContext.Default.PushReplyRequest),
            cancellationToken);

    public Task<PushReplyResult> MarkReadAsync(PushReplyTarget target, string? trustedFingerprint, int chatId,
        int messageId, CancellationToken cancellationToken = default) =>
        PostAsync(target, trustedFingerprint, PushEndpoints.MarkRead,
            JsonContent.Create(new PushMarkReadRequest(chatId, messageId),
                ProtocolJsonContext.Default.PushMarkReadRequest),
            cancellationToken);

    private async Task<PushReplyResult> PostAsync(PushReplyTarget target, string? trustedFingerprint, string path,
        HttpContent content, CancellationToken cancellationToken)
    {
        // A client of its own: the certificate trusted differs from one coordinator to another
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = Timeout,
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                    ServerCertificateCheck.IsTrusted(certificate, errors, trustedFingerprint, out _)
            }
        };
        using var client = new HttpClient(handler) { Timeout = Timeout };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new UriBuilder(Uri.UriSchemeHttps, target.Address, target.Port, path).Uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.ReplyToken);
        request.Content = content;

        try
        {
            // Buffered, so that the server is told its length
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            using var response = await client.SendAsync(request, cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.NoContent or HttpStatusCode.OK => PushReplyResult.Sent,
                HttpStatusCode.Unauthorized => PushReplyResult.Unauthorized,
                HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound => PushReplyResult.Rejected,
                HttpStatusCode.TooManyRequests => PushReplyResult.TooManyRequests,
                _ => PushReplyResult.ConnectionFailed
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            LogSendFailed(exception, target.Address, target.Port);
            return PushReplyResult.ConnectionFailed;
        }
    }

    [LoggerMessage(LogLevel.Warning, "A request from a notification did not reach {Address}:{Port}")]
    private partial void LogSendFailed(Exception exception, string address, int port);
}
