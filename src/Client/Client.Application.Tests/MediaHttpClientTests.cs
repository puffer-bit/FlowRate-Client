using System.Net;
using System.Text;
using Client.Api.Transport.HTTP;
using Client.Core.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models.Media;
using Xunit;

namespace Client.Application.Tests;

public class MediaHttpClientTests
{
    private const int Megabyte = 1024 * 1024;

    private readonly FakeTusServer _server = new();
    private readonly MediaHttpClient _client;
    private readonly byte[] _content = new byte[2 * Megabyte + 5000];
    private string? _token = "token";
    private int _tokenRefreshes;

    public MediaHttpClientTests()
    {
        new Random(7).NextBytes(_content);
        _client = new MediaHttpClient(_server, new Uri("https://server:1/"), (refresh, _) =>
        {
            if (refresh)
            {
                _tokenRefreshes++;
                _token = _server.Token;
            }

            return Task.FromResult(_token);
        }, NullLogger.Instance, (_, _) => Task.CompletedTask);
    }

    private MediaUpload Upload(string? resume = null, bool seekable = true) => new(3, MediaUploadKind.Image,
        "image/png", _content.Length,
        () => Task.FromResult<Stream>(seekable ? new MemoryStream(_content) : new ForwardOnlyStream(_content)), resume);

    private Task<MediaUploadOutcome> UploadAsync(MediaUpload? upload = null, IProgress<long>? progress = null) =>
        _client.UploadAsync(upload ?? Upload(), progress, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Upload_SendsTheFileInPartsAndDescribesIt()
    {
        var reported = new List<long>();

        var outcome = await UploadAsync(progress: new Collector(reported));

        Assert.Equal(MediaTransferResult.NoError, outcome.Result);
        Assert.Equal(_content, _server.ContentOf(outcome.UploadId!));
        Assert.Equal(3, _server.Patches);
        Assert.Equal(_content.Length, reported[^1]);
        Assert.Equal("3", _server.Metadata[MediaUploadMetadata.ChatId]);
        Assert.Equal("image/png", _server.Metadata[MediaUploadMetadata.MimeType]);
        Assert.Equal(MediaUploadMetadata.KindImage, _server.Metadata[MediaUploadMetadata.Kind]);
    }

    [Fact]
    public async Task Upload_OfAnAvatar_NamesNoChat()
    {
        var outcome = await UploadAsync(Upload() with { ChatId = null, Kind = MediaUploadKind.Avatar });

        Assert.Equal(MediaTransferResult.NoError, outcome.Result);
        Assert.Equal(MediaUploadMetadata.KindAvatar, _server.Metadata[MediaUploadMetadata.Kind]);
        Assert.False(_server.Metadata.ContainsKey(MediaUploadMetadata.ChatId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_ContinuesAfterTheConnectionBreaks(bool seekable)
    {
        _server.FailPatches = [2, 3];

        var outcome = await UploadAsync(Upload(seekable: seekable));

        Assert.Equal(MediaTransferResult.NoError, outcome.Result);
        Assert.Equal(_content, _server.ContentOf(outcome.UploadId!));
        // The first megabyte went once: the two failed parts are the only ones repeated
        Assert.Equal(5, _server.Patches);
    }

    [Fact]
    public async Task Upload_DoesNotRepeatAPartTheServerTookBeforeTheAnswerWasLost()
    {
        _server.LoseAnswerOfPatches = [1];

        var outcome = await UploadAsync();

        Assert.Equal(MediaTransferResult.NoError, outcome.Result);
        Assert.Equal(_content, _server.ContentOf(outcome.UploadId!));
        Assert.Equal(3, _server.Patches);
    }

    [Fact]
    public async Task Upload_GivesUpWhenTheServerStaysAway_AndIsContinuedLater()
    {
        _server.FailPatches = Enumerable.Range(2, 100).ToHashSet();

        var failed = await UploadAsync();

        Assert.Equal(MediaTransferResult.ConnectionFailed, failed.Result);
        Assert.NotNull(failed.UploadId);

        _server.FailPatches = [];
        var patchesBefore = _server.Patches;
        var resumed = await UploadAsync(Upload(resume: failed.UploadId));

        Assert.Equal(MediaTransferResult.NoError, resumed.Result);
        Assert.Equal(failed.UploadId, resumed.UploadId);
        Assert.Equal(_content, _server.ContentOf(resumed.UploadId!));
        Assert.Equal(2, _server.Patches - patchesBefore);
        Assert.Equal(1, _server.Creations);
    }

    [Fact]
    public async Task Upload_OfAnUnknownUpload_IsNotFound()
    {
        var outcome = await UploadAsync(Upload(resume: "gone"));

        Assert.Equal(MediaTransferResult.NotFound, outcome.Result);
        Assert.Null(outcome.UploadId);
    }

    [Fact]
    public async Task RefusedToken_IsReplacedOnce()
    {
        await UploadAsync();
        _server.Token = "changed-on-the-server";

        var outcome = await UploadAsync();

        Assert.Equal(MediaTransferResult.NoError, outcome.Result);
        Assert.Equal(1, _tokenRefreshes);
    }

    [Fact]
    public async Task Upload_WithoutAToken_IsUnauthorized()
    {
        _token = null;
        _server.Token = null;

        Assert.Equal(MediaTransferResult.Unauthorized, (await UploadAsync()).Result);
        Assert.Equal(0, _server.Creations);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, MediaTransferResult.TooLarge)]
    [InlineData(HttpStatusCode.Forbidden, MediaTransferResult.NotAllowed)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, MediaTransferResult.TypeNotAllowed)]
    [InlineData(HttpStatusCode.TooManyRequests, MediaTransferResult.TooManyUploads)]
    [InlineData(HttpStatusCode.InsufficientStorage, MediaTransferResult.StorageFull)]
    [InlineData(HttpStatusCode.NotFound, MediaTransferResult.NotFound)]
    public async Task Upload_TheServerRefuses_SaysWhy(HttpStatusCode status, MediaTransferResult expected)
    {
        _server.RefuseCreationWith = status;

        Assert.Equal(expected, (await UploadAsync()).Result);
    }

    [Fact]
    public async Task Upload_Canceled_KeepsItsIdForLater()
    {
        using var canceled = new CancellationTokenSource();
        _server.OnPatch = number =>
        {
            if (number == 2)
                canceled.Cancel();
        };

        var outcome = await _client.UploadAsync(Upload(), null, canceled.Token);

        Assert.Equal(MediaTransferResult.Canceled, outcome.Result);
        Assert.NotNull(outcome.UploadId);
    }

    [Fact]
    public async Task Download_WritesTheImage()
    {
        _server.Images["/media/images/12?variant=preview"] = _content;
        using var destination = new MemoryStream();

        var result = await _client.DownloadImageAsync(12, true, destination, null, TestContext.Current.CancellationToken);

        Assert.Equal(MediaTransferResult.NoError, result);
        Assert.Equal(_content, destination.ToArray());
    }

    [Fact]
    public async Task Upload_OfAFile_IsDescribedAsOne()
    {
        var outcome = await UploadAsync(Upload() with { Kind = MediaUploadKind.File, MimeType = "application/pdf" });

        Assert.Equal(MediaTransferResult.NoError, outcome.Result);
        Assert.Equal(MediaUploadMetadata.KindFile, _server.Metadata[MediaUploadMetadata.Kind]);
        Assert.Equal("application/pdf", _server.Metadata[MediaUploadMetadata.MimeType]);
    }

    [Fact]
    public async Task Download_WritesTheFile()
    {
        _server.Images["/media/files/31"] = _content;
        using var destination = new MemoryStream();
        var reported = new List<long>();

        var result = await _client.DownloadFileAsync(31, destination, new Collector(reported),
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaTransferResult.NoError, result);
        Assert.Equal(_content, destination.ToArray());
        Assert.Equal(_content.Length, reported[^1]);
    }

    [Fact]
    public async Task Download_WritesTheAvatar()
    {
        _server.Images["/media/avatars/0a1b"] = _content;
        using var destination = new MemoryStream();

        var result = await _client.DownloadAvatarAsync("0a1b", destination, TestContext.Current.CancellationToken);

        Assert.Equal(MediaTransferResult.NoError, result);
        Assert.Equal(_content, destination.ToArray());
    }

    [Fact]
    public async Task Download_OfAMissingImage_IsNotFound()
    {
        using var destination = new MemoryStream();

        Assert.Equal(MediaTransferResult.NotFound,
            await _client.DownloadImageAsync(12, false, destination, null, TestContext.Current.CancellationToken));
        Assert.Equal(0, destination.Length);
    }

    private sealed class Collector(List<long> values) : IProgress<long>
    {
        public void Report(long value) => values.Add(value);
    }

    private sealed class ForwardOnlyStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }
    }

    /// <summary>
    /// The part of a tus server the client uses, in memory, with the failures of a bad connection.
    /// </summary>
    private sealed class FakeTusServer : HttpMessageHandler
    {
        private readonly Dictionary<string, (long Length, MemoryStream Content)> _uploads = new();

        public string? Token { get; set; } = "token";
        public int Creations { get; private set; }
        public int Patches { get; private set; }
        public Dictionary<string, string> Metadata { get; } = new();
        public Dictionary<string, byte[]> Images { get; } = new();

        /// <summary>Numbers of the PATCH requests, from 1, that never reach the server.</summary>
        public HashSet<int> FailPatches { get; set; } = [];
        /// <summary>Numbers of the PATCH requests the server takes but whose answer is lost.</summary>
        public HashSet<int> LoseAnswerOfPatches { get; set; } = [];
        public HttpStatusCode? RefuseCreationWith { get; set; }
        public Action<int>? OnPatch { get; set; }

        public byte[] ContentOf(string id) => _uploads[id].Content.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Headers.Authorization is not { Scheme: "Bearer" } authorization || authorization.Parameter != Token)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get)
                return Images.TryGetValue(path, out var image)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.Method == HttpMethod.Post)
            {
                if (RefuseCreationWith is { } status)
                    return new HttpResponseMessage(status);

                Creations++;
                foreach (var pair in request.Headers.GetValues("Upload-Metadata").Single().Split(','))
                {
                    var parts = pair.Split(' ');
                    Metadata[parts[0]] = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                }

                var id = $"upload{_uploads.Count + 1}";
                _uploads[id] = (long.Parse(request.Headers.GetValues("Upload-Length").Single()), new MemoryStream());
                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.Location = new Uri($"{MediaEndpoints.Uploads}/{id}", UriKind.Relative);
                return created;
            }

            if (!_uploads.TryGetValue(path.Split('/')[^1], out var upload))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.Method == HttpMethod.Head)
                return WithOffset(new HttpResponseMessage(HttpStatusCode.OK), upload.Content.Length);

            var number = ++Patches;
            OnPatch?.Invoke(number);
            cancellationToken.ThrowIfCancellationRequested();
            if (FailPatches.Contains(number))
                throw new HttpRequestException("connection reset");

            if (long.Parse(request.Headers.GetValues("Upload-Offset").Single()) != upload.Content.Length)
                return new HttpResponseMessage(HttpStatusCode.Conflict);

            await request.Content!.CopyToAsync(upload.Content, cancellationToken);
            if (upload.Content.Length > upload.Length)
                return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge);
            if (LoseAnswerOfPatches.Contains(number))
                throw new HttpRequestException("connection reset");

            return WithOffset(new HttpResponseMessage(HttpStatusCode.NoContent), upload.Content.Length);
        }

        private static HttpResponseMessage WithOffset(HttpResponseMessage response, long offset)
        {
            response.Headers.Add("Upload-Offset", offset.ToString());
            return response;
        }
    }
}
