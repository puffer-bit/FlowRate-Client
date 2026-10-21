using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Client.Core.Chat;
using Microsoft.Extensions.Logging;
using Shared.Models.Media;

namespace Client.Api.Transport.HTTP;

/// <summary>
/// The HTTP side of a coordinator: uploads by the tus protocol and downloads of message images and avatars.
/// It talks to the address of the web socket and trusts the same certificate.
/// </summary>
internal sealed partial class MediaHttpClient : IDisposable
{
    /// <param name="refresh">True when the server refused the token it was given last.</param>
    public delegate Task<string?> TokenSource(bool refresh, CancellationToken cancellationToken);

    private const string TusVersion = "1.0.0";
    private const int ChunkSize = 1024 * 1024;

    // A chunk has this long to arrive, which a slow mobile link still manages
    private static readonly TimeSpan ChunkTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // Pauses before a failed part of an upload is tried again; the upload gives up after the last one
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15)
    ];

    private readonly HttpClient _http;
    private readonly TokenSource _tokens;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MediaHttpClient(string address, int port, string? trustedFingerprint, TokenSource tokens, ILogger logger)
        : this(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                    ServerCertificateCheck.IsTrusted(certificate, errors, trustedFingerprint, out _)
            }
        }, new Uri($"https://{address}:{port}/"), tokens, logger, Task.Delay)
    {
    }

    internal MediaHttpClient(HttpMessageHandler handler, Uri baseAddress, TokenSource tokens, ILogger logger,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        // Each request has its own time limit: an upload as a whole may take any time
        _http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        _tokens = tokens;
        _logger = logger;
        _delay = delay;
    }

    public async Task<MediaUploadOutcome> UploadAsync(MediaUpload upload, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var uploadId = upload.ResumeUploadId;
        try
        {
            long offset;
            if (uploadId == null)
            {
                var (created, id) = await CreateAsync(upload, cancellationToken);
                if (created != MediaTransferResult.NoError)
                    return new MediaUploadOutcome(created, null);

                uploadId = id;
                offset = 0;
            }
            else
            {
                var (found, known) = await GetOffsetAsync(uploadId, cancellationToken);
                if (found != MediaTransferResult.NoError)
                    return new MediaUploadOutcome(found, null);

                offset = known;
            }

            progress?.Report(offset);
            return new MediaUploadOutcome(
                await SendContentAsync(upload, uploadId!, offset, progress, cancellationToken), uploadId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MediaUploadOutcome(MediaTransferResult.Canceled, uploadId);
        }
    }

    public Task<MediaTransferResult> DownloadImageAsync(int imageId, bool preview, Stream destination,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var variant = preview ? MediaEndpoints.VariantPreview : MediaEndpoints.VariantOriginal;
        return DownloadAsync(string.Create(CultureInfo.InvariantCulture,
                $"{MediaEndpoints.Images}/{imageId}?{MediaEndpoints.VariantParameter}={variant}"),
            destination, progress, cancellationToken);
    }

    public Task<MediaTransferResult> DownloadAvatarAsync(string avatarId, Stream destination,
        CancellationToken cancellationToken) =>
        DownloadAsync($"{MediaEndpoints.Avatars}/{Uri.EscapeDataString(avatarId)}", destination, null,
            cancellationToken);

    private async Task<MediaTransferResult> DownloadAsync(string path, Stream destination,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), RequestTimeout,
                cancellationToken, HttpCompletionOption.ResponseHeadersRead);
            if (response == null)
                return MediaTransferResult.ConnectionFailed;
            if (!response.IsSuccessStatusCode)
                return ResultOf(response.StatusCode);

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                long received = 0;
                while (true)
                {
                    // The limit is on each read, so a stalled download ends while a long one goes on
                    using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    stalled.CancelAfter(RequestTimeout);

                    var read = await content.ReadAsync(buffer, stalled.Token);
                    if (read == 0)
                        return MediaTransferResult.NoError;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(received);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MediaTransferResult.Canceled;
        }
        // The server stopped sending in the middle
        catch (Exception exception) when (exception is OperationCanceledException || IsConnectionFailure(exception))
        {
            LogRequestFailed(path, exception.Message);
            return MediaTransferResult.ConnectionFailed;
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<(MediaTransferResult Result, string? UploadId)> CreateAsync(MediaUpload upload,
        CancellationToken cancellationToken)
    {
        var entries = new List<string>
        {
            Metadata(MediaUploadMetadata.MimeType, upload.MimeType),
            Metadata(MediaUploadMetadata.Kind, upload.Kind switch
            {
                MediaUploadKind.ImagePreview => MediaUploadMetadata.KindImagePreview,
                MediaUploadKind.Avatar => MediaUploadMetadata.KindAvatar,
                _ => MediaUploadMetadata.KindImage
            })
        };
        if (upload.ChatId is { } chatId)
            entries.Add(Metadata(MediaUploadMetadata.ChatId, chatId.ToString(CultureInfo.InvariantCulture)));

        var metadata = string.Join(',', entries);

        using var response = await SendWithRetriesAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, MediaEndpoints.Uploads);
            request.Headers.Add("Tus-Resumable", TusVersion);
            request.Headers.Add("Upload-Length", upload.Size.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("Upload-Metadata", metadata);
            return request;
        }, RequestTimeout, cancellationToken);

        if (response == null)
            return (MediaTransferResult.ConnectionFailed, null);
        if (response.StatusCode != HttpStatusCode.Created)
            return (ResultOf(response.StatusCode), null);

        // The id is the last segment of the address the server gave the upload
        var id = response.Headers.Location?.OriginalString.TrimEnd('/').Split('/')[^1];
        return string.IsNullOrEmpty(id) ? (MediaTransferResult.Failed, null) : (MediaTransferResult.NoError, id);
    }

    /// <summary>How much of the upload the server holds.</summary>
    private async Task<(MediaTransferResult Result, long Offset)> GetOffsetAsync(string uploadId,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetriesAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Head, UploadPath(uploadId));
            request.Headers.Add("Tus-Resumable", TusVersion);
            return request;
        }, RequestTimeout, cancellationToken);

        if (response == null)
            return (MediaTransferResult.ConnectionFailed, 0);
        if (!response.IsSuccessStatusCode)
            // An upload the server does not let this user see is as good as gone
            return (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Gone
                ? MediaTransferResult.NotFound
                : ResultOf(response.StatusCode), 0);

        return TryReadOffset(response, out var offset)
            ? (MediaTransferResult.NoError, offset)
            : (MediaTransferResult.Failed, 0);
    }

    private async Task<MediaTransferResult> SendContentAsync(MediaUpload upload, string uploadId, long offset,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        Stream? content = null;
        long position = 0;
        var failures = 0;

        try
        {
            while (offset < upload.Size)
            {
                // After a failure the source is opened again: what it gave last may be lost with the request
                if (content == null || position != offset)
                {
                    if (content != null)
                        await content.DisposeAsync();

                    content = await upload.OpenContent();
                    position = await SkipAsync(content, offset, buffer, cancellationToken);
                    if (position != offset)
                        return MediaTransferResult.Failed;
                }

                var length = await content.ReadAtLeastAsync(
                    buffer.AsMemory(0, (int)Math.Min(ChunkSize, upload.Size - offset)),
                    (int)Math.Min(ChunkSize, upload.Size - offset), throwOnEndOfStream: false, cancellationToken);
                if (length == 0)
                    return MediaTransferResult.Failed;

                position += length;
                var chunkOffset = offset;

                using var response = await SendAsync(() =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Patch, UploadPath(uploadId))
                    {
                        Content = new ByteArrayContent(buffer, 0, length)
                    };
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
                    request.Headers.Add("Tus-Resumable", TusVersion);
                    request.Headers.Add("Upload-Offset", chunkOffset.ToString(CultureInfo.InvariantCulture));
                    return request;
                }, ChunkTimeout, cancellationToken);

                if (response is { StatusCode: HttpStatusCode.NoContent } && TryReadOffset(response, out var accepted))
                {
                    offset = accepted;
                    failures = 0;
                    progress?.Report(offset);
                    continue;
                }

                // Lost on the way, or the server holds another amount than this side thinks: it is asked
                if (response == null || response.StatusCode == HttpStatusCode.Conflict
                                     || IsTransient(response.StatusCode))
                {
                    if (failures == RetryDelays.Length)
                        return MediaTransferResult.ConnectionFailed;

                    await _delay(RetryDelays[failures++], cancellationToken);

                    var (found, known) = await GetOffsetAsync(uploadId, cancellationToken);
                    if (found != MediaTransferResult.NoError)
                        return found;

                    offset = known;
                    progress?.Report(offset);
                    continue;
                }

                return response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Gone
                    ? MediaTransferResult.NotFound
                    : ResultOf(response.StatusCode);
            }

            return MediaTransferResult.NoError;
        }
        finally
        {
            if (content != null)
                await content.DisposeAsync();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<long> SkipAsync(Stream content, long count, byte[] buffer, CancellationToken cancellationToken)
    {
        if (count == 0)
            return 0;

        if (content.CanSeek)
        {
            content.Seek(count, SeekOrigin.Begin);
            return content.Position;
        }

        long skipped = 0;
        while (skipped < count)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count - skipped)),
                cancellationToken);
            if (read == 0)
                break;
            skipped += read;
        }

        return skipped;
    }

    /// <summary>
    /// A request that changes nothing when repeated, tried again after a pause while the server
    /// cannot be reached. Null when it still cannot.
    /// </summary>
    private async Task<HttpResponseMessage?> SendWithRetriesAsync(Func<HttpRequestMessage> createRequest,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        for (var failures = 0;; failures++)
        {
            var response = await SendAsync(createRequest, timeout, cancellationToken);
            if (response != null && !IsTransient(response.StatusCode))
                return response;

            response?.Dispose();
            if (failures == RetryDelays.Length)
                return null;

            await _delay(RetryDelays[failures], cancellationToken);
        }
    }

    /// <summary>
    /// Sends with the media token, asking for a new one once when the server refuses it.
    /// Null when the server could not be reached or did not answer in time.
    /// </summary>
    private async Task<HttpResponseMessage?> SendAsync(Func<HttpRequestMessage> createRequest, TimeSpan timeout,
        CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        for (var refresh = false;; refresh = true)
        {
            var token = await _tokens(refresh, cancellationToken);
            if (token == null)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            using var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var timedOut = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timedOut.CancelAfter(timeout);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, completion, timedOut.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogRequestFailed(request.RequestUri?.OriginalString ?? string.Empty, "timed out");
                return null;
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                LogRequestFailed(request.RequestUri?.OriginalString ?? string.Empty, exception.Message);
                return null;
            }

            if (response.StatusCode != HttpStatusCode.Unauthorized || refresh)
                return response;

            response.Dispose();
        }
    }

    private static bool IsConnectionFailure(Exception exception) => exception is HttpRequestException or IOException;

    /// <summary>An answer that says nothing about the request itself; the same request may pass later.</summary>
    private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout
        or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.GatewayTimeout;

    private static bool TryReadOffset(HttpResponseMessage response, out long offset)
    {
        offset = 0;
        return response.Headers.TryGetValues("Upload-Offset", out var values)
               && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }

    private static string UploadPath(string uploadId) => $"{MediaEndpoints.Uploads}/{Uri.EscapeDataString(uploadId)}";

    private static string Metadata(string key, string value) =>
        $"{key} {Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}";

    private static MediaTransferResult ResultOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => MediaTransferResult.Unauthorized,
        HttpStatusCode.Forbidden => MediaTransferResult.NotAllowed,
        HttpStatusCode.NotFound => MediaTransferResult.NotFound,
        HttpStatusCode.RequestEntityTooLarge => MediaTransferResult.TooLarge,
        HttpStatusCode.UnsupportedMediaType => MediaTransferResult.TypeNotAllowed,
        HttpStatusCode.TooManyRequests => MediaTransferResult.TooManyUploads,
        HttpStatusCode.InsufficientStorage => MediaTransferResult.StorageFull,
        _ => MediaTransferResult.Failed
    };

    [LoggerMessage(LogLevel.Debug, "Media request {Path} failed: {Reason}")]
    private partial void LogRequestFailed(string path, string reason);
}
