using Client.Api.Transport.HTTP;
using Client.Core.Chat;
using Shared.Enums;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Responses.Coordinator;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    // A token is asked for anew this long before it expires, so a request does not start with a stale one
    private static readonly TimeSpan MediaTokenMargin = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _mediaTokenLock = new(1, 1);
    private MediaHttpClient? _media;
    private string? _mediaToken;
    private DateTime _mediaTokenExpiresAt;

    public Task<MediaUploadOutcome> UploadMedia(MediaUpload upload, IProgress<long>? progress,
        CancellationToken cancellationToken) =>
        _media is { } media
            ? media.UploadAsync(upload, progress, cancellationToken)
            : Task.FromResult(new MediaUploadOutcome(MediaTransferResult.ConnectionFailed, upload.ResumeUploadId));

    public Task<MediaTransferResult> DownloadChatImage(int imageId, bool preview, Stream destination,
        IProgress<long>? progress, CancellationToken cancellationToken) =>
        _media is { } media
            ? media.DownloadImageAsync(imageId, preview, destination, progress, cancellationToken)
            : Task.FromResult(MediaTransferResult.ConnectionFailed);

    public Task<MediaTransferResult> DownloadChatFile(int fileId, Stream destination, IProgress<long>? progress,
        CancellationToken cancellationToken) =>
        _media is { } media
            ? media.DownloadFileAsync(fileId, destination, progress, cancellationToken)
            : Task.FromResult(MediaTransferResult.ConnectionFailed);

    public Task<MediaTransferResult> DownloadAvatar(string avatarId, Stream destination,
        CancellationToken cancellationToken) =>
        _media is { } media
            ? media.DownloadAvatarAsync(avatarId, destination, cancellationToken)
            : Task.FromResult(MediaTransferResult.ConnectionFailed);

    /// <summary>
    /// The media endpoints live at the address of the web socket and are reached once it is connected.
    /// </summary>
    private void OpenMedia(string address, int port, string? trustedFingerprint)
    {
        CloseMedia();
        _media = new MediaHttpClient(address, port, trustedFingerprint, GetMediaTokenAsync, _logger);
    }

    private void CloseMedia()
    {
        Interlocked.Exchange(ref _media, null)?.Dispose();
        _mediaToken = null;
    }

    private async Task<string?> GetMediaTokenAsync(bool refresh, CancellationToken cancellationToken)
    {
        await _mediaTokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _mediaToken != null && DateTime.UtcNow < _mediaTokenExpiresAt - MediaTokenMargin)
                return _mediaToken;

            var response = await SendRequestAsync<MediaTokenResponse>(new MediaTokenRequest());

            _mediaTokenExpiresAt = response.ExpiresAt.ToUniversalTime();
            return _mediaToken = response.Token;
        }
        // No answer, a rejection or a lost connection: there is no token, and the transfer reports it
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogMediaTokenFailed(exception.Message);
            return _mediaToken = null;
        }
        finally
        {
            _mediaTokenLock.Release();
        }
    }
}
