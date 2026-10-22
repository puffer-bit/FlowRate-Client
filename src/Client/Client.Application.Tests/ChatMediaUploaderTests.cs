using System.Reflection;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Core.Chat;
using Shared.Validation;
using Xunit;

namespace Client.Application.Tests;

public class ChatMediaUploaderTests
{
    private static readonly byte[] Content = [1, 2, 3, 4, 5];

    private readonly FakeSession _fake;
    private readonly ICoordinatorSession _session;
    private readonly FixedPreviewer _previewer = new();
    private readonly ChatMediaUploader _uploader;
    private readonly ChatImageUploadState _state = new();

    public ChatMediaUploaderTests()
    {
        _session = DispatchProxy.Create<ICoordinatorSession, FakeSession>();
        _fake = (FakeSession)(object)_session;
        _uploader = new ChatMediaUploader(_previewer);
    }

    private Task<ChatMediaUploadResult> UploadAsync(string fileName = "cat.png", IProgress<double>? progress = null) =>
        _uploader.UploadImageAsync(_session, 3,
            new ChatMediaSource(fileName, Content.Length, () => Task.FromResult<Stream>(new MemoryStream(Content))),
            _state, progress, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Image_GoesAsItsReducedCopyAndThenItself()
    {
        var reported = new List<double>();

        var result = await UploadAsync(progress: new Collector(reported));

        Assert.Equal(ChatMediaUploadResult.NoError, result);
        Assert.Collection(_fake.Uploads,
            preview =>
            {
                Assert.Equal(MediaUploadKind.ImagePreview, preview.Kind);
                Assert.Equal("image/webp", preview.MimeType);
                Assert.Equal(FixedPreviewer.PreviewBytes.Length, preview.Size);
            },
            original =>
            {
                Assert.Equal(MediaUploadKind.Image, original.Kind);
                Assert.Equal("image/png", original.MimeType);
                Assert.Equal(Content.Length, original.Size);
                Assert.Equal(3, original.ChatId);
            });

        var image = _state.Image!;
        Assert.Equal("upload1", image.PreviewUploadId);
        Assert.Equal("upload2", image.UploadId);
        Assert.Equal("cat.png", image.FileName);
        Assert.Equal((640, 480), (image.Width, image.Height));
        Assert.Equal(1, reported[^1]);
    }

    [Fact]
    public async Task FileThatIsNotAnImage_IsNotUploaded()
    {
        _previewer.Result = null;

        Assert.Equal(ChatMediaUploadResult.NotAnImage, await UploadAsync());
        Assert.Empty(_fake.Uploads);
    }

    [Fact]
    public async Task ImageOfAFormatTheChatDoesNotCarry_IsNotUploaded()
    {
        _previewer.Result = _previewer.Result! with { MimeType = "image/bmp" };

        Assert.Equal(ChatMediaUploadResult.NotAnImage, await UploadAsync());
        Assert.Empty(_fake.Uploads);
    }

    [Fact]
    public async Task InterruptedImage_IsContinuedByTheNextAttempt()
    {
        _fake.Results.Enqueue(MediaTransferResult.NoError);
        _fake.Results.Enqueue(MediaTransferResult.ConnectionFailed);

        Assert.Equal(ChatMediaUploadResult.ConnectionFailed, await UploadAsync());
        Assert.Null(_state.Image);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadAsync());

        // The reduced copy is not sent again, and the image continues the upload it began
        Assert.Equal(3, _fake.Uploads.Count);
        Assert.Equal(MediaUploadKind.Image, _fake.Uploads[2].Kind);
        Assert.Equal("upload2", _fake.Uploads[2].ResumeUploadId);
        Assert.Equal("upload2", _state.Image!.UploadId);
        Assert.Equal(1, _previewer.Calls);
    }

    [Fact]
    public async Task UploadTheServerDropped_StartsOver()
    {
        _fake.Results.Enqueue(MediaTransferResult.NoError);
        _fake.Results.Enqueue(MediaTransferResult.ConnectionFailed);
        await UploadAsync();
        _fake.Results.Enqueue(MediaTransferResult.NotFound);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadAsync());

        Assert.Null(_fake.Uploads[^1].ResumeUploadId);
        Assert.Equal("upload4", _state.Image!.UploadId);
    }

    [Fact]
    public async Task UploadedImage_IsNotUploadedAgain()
    {
        await UploadAsync();

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadAsync());
        Assert.Equal(2, _fake.Uploads.Count);
    }

    [Theory]
    [InlineData(MediaTransferResult.TooLarge, ChatMediaUploadResult.TooLarge)]
    [InlineData(MediaTransferResult.NotAllowed, ChatMediaUploadResult.NotAllowed)]
    [InlineData(MediaTransferResult.Unauthorized, ChatMediaUploadResult.ConnectionFailed)]
    [InlineData(MediaTransferResult.StorageFull, ChatMediaUploadResult.StorageFull)]
    [InlineData(MediaTransferResult.Canceled, ChatMediaUploadResult.Canceled)]
    public async Task Refusal_IsPassedOn(MediaTransferResult refusal, ChatMediaUploadResult expected)
    {
        _fake.Results.Enqueue(refusal);

        Assert.Equal(expected, await UploadAsync());
    }

    [Fact]
    public async Task FileName_IsMadeAcceptableForTheServer()
    {
        await UploadAsync(new string('a', 300) + "\u0007.jpeg");

        var name = _state.Image!.FileName;
        Assert.Equal(Limits.FileNameMaxLength, name.Length);
        Assert.EndsWith(".jpeg", name);
        Assert.DoesNotContain('\u0007', name);
    }

    private readonly ChatFileUploadState _fileState = new();

    private Task<ChatMediaUploadResult> UploadFileAsync(string fileName = "report.pdf", long? size = null) =>
        _uploader.UploadFileAsync(_session, 3,
            new ChatMediaSource(fileName, size ?? Content.Length, () => Task.FromResult<Stream>(new MemoryStream(Content))),
            _fileState, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task File_GoesAsItIs_WithTheTypeOfItsName()
    {
        Assert.Equal(ChatMediaUploadResult.NoError, await UploadFileAsync());

        var upload = Assert.Single(_fake.Uploads);
        Assert.Equal(MediaUploadKind.File, upload.Kind);
        Assert.Equal("application/pdf", upload.MimeType);
        Assert.Equal(Content.Length, upload.Size);
        Assert.Equal(3, upload.ChatId);
        Assert.Equal("upload1", _fileState.File!.UploadId);
        Assert.Equal("report.pdf", _fileState.File.FileName);
        Assert.Equal(0, _previewer.Calls);
    }

    [Fact]
    public async Task FileOfAnUnknownType_GoesAsPlainBytes()
    {
        await UploadFileAsync("data.xyz123");

        Assert.Equal(FileMimeTypes.Default, Assert.Single(_fake.Uploads).MimeType);
    }

    [Fact]
    public async Task EmptyFile_IsNotUploaded()
    {
        Assert.Equal(ChatMediaUploadResult.Empty, await UploadFileAsync(size: 0));
        Assert.Empty(_fake.Uploads);
    }

    [Fact]
    public async Task InterruptedFile_IsContinuedByTheNextAttempt()
    {
        _fake.Results.Enqueue(MediaTransferResult.ConnectionFailed);
        Assert.Equal(ChatMediaUploadResult.ConnectionFailed, await UploadFileAsync());

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadFileAsync());
        Assert.Equal("upload1", _fake.Uploads[1].ResumeUploadId);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadFileAsync());
        Assert.Equal(2, _fake.Uploads.Count);
    }

    private sealed class Collector(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }

    private sealed class FixedPreviewer : IImagePreviewer
    {
        public static readonly byte[] PreviewBytes = [9, 9, 9];

        public ImagePreview? Result { get; set; } = new("image/png", 640, 480, PreviewBytes, "image/webp");
        public int Calls { get; private set; }

        public Task<ImagePreview?> CreateAsync(Func<Task<Stream>> openContent, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    /// <summary>
    /// A coordinator session that only takes uploads; each gets the next id unless a result is queued for it.
    /// </summary>
    public class FakeSession : DispatchProxy
    {
        public List<MediaUpload> Uploads { get; } = [];
        public Queue<MediaTransferResult> Results { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name != nameof(ICoordinatorSession.UploadMedia))
                throw new NotSupportedException(targetMethod.Name);

            var upload = (MediaUpload)args![0]!;
            Uploads.Add(upload);

            var result = Results.TryDequeue(out var queued) ? queued : MediaTransferResult.NoError;
            var id = result is MediaTransferResult.NoError or MediaTransferResult.ConnectionFailed or MediaTransferResult.Canceled
                ? upload.ResumeUploadId ?? $"upload{Uploads.Count}"
                : null;
            if (result == MediaTransferResult.NoError)
                ((IProgress<long>?)args[1])?.Report(upload.Size);

            return Task.FromResult(new MediaUploadOutcome(result, id));
        }
    }
}
