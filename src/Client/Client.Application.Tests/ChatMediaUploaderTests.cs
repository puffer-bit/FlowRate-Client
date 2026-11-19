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

    [Fact]
    public async Task FileAnotherProgramHoldsLocked_IsReportedUnreadable()
    {
        var result = await _uploader.UploadFileAsync(_session, 3,
            new ChatMediaSource("app-20261027.txt", Content.Length,
                () => throw new IOException("The process cannot access the file because it is being used by another process.")),
            _fileState, null, TestContext.Current.CancellationToken);

        Assert.Equal(ChatMediaUploadResult.Unreadable, result);
        Assert.Null(_fileState.File);
    }

    [Fact]
    public async Task ImageThatCannotBeRead_IsReportedUnreadable()
    {
        var result = await _uploader.UploadImageAsync(_session, 3,
            new ChatMediaSource("cat.png", Content.Length, () => throw new UnauthorizedAccessException()),
            _state, null, TestContext.Current.CancellationToken);

        Assert.Equal(ChatMediaUploadResult.Unreadable, result);
        Assert.Null(_state.Image);
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

        public ImagePreview? FrameResult { get; set; } = new("image/webp", 320, 180, PreviewBytes, "image/webp");
        public VideoFrameImage? LastFrame { get; private set; }

        public Task<ImagePreview?> CreateAsync(VideoFrameImage frame, CancellationToken cancellationToken = default)
        {
            LastFrame = frame;
            return Task.FromResult(FrameResult);
        }
    }

    private sealed class FixedFrames : IVideoFrameGrabber
    {
        public bool IsAvailable { get; set; } = true;
        public VideoFrameImage? Result { get; set; } = new(new byte[2 * 2 * 4], 2, 2, 90);
        public List<string> Paths { get; } = [];

        public Task<VideoFrameImage?> GrabAsync(string path, int maxSide, CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            return Task.FromResult(Result);
        }
    }

    private readonly FixedFrames _frames = new();

    private Task<ChatMediaUploadResult> UploadVideoAsync(ChatMediaUploader uploader, string fileName = "clip.mp4",
        string? localPath = "/videos/clip.mp4") =>
        uploader.UploadFileAsync(_session, 3,
            new ChatMediaSource(fileName, Content.Length, () => Task.FromResult<Stream>(new MemoryStream(Content)),
                localPath),
            _fileState, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Video_GoesWithAFrameToBeShownBy()
    {
        var uploader = new ChatMediaUploader(_previewer, _frames);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadVideoAsync(uploader));

        Assert.Collection(_fake.Uploads,
            video => Assert.Equal(MediaUploadKind.File, video.Kind),
            frame =>
            {
                Assert.Equal(MediaUploadKind.ImagePreview, frame.Kind);
                Assert.Equal("image/webp", frame.MimeType);
            });
        Assert.Equal(["/videos/clip.mp4"], _frames.Paths);
        Assert.Equal(90, _previewer.LastFrame!.Rotation);
        Assert.Equal("upload1", _fileState.File!.UploadId);
        Assert.Equal("upload2", _fileState.File.PreviewUploadId);
    }

    [Theory]
    [InlineData("notes.pdf", "/files/notes.pdf")]
    [InlineData("clip.mp4", null)]
    public async Task FileThatIsNoVideoOnDisk_GoesWithoutAFrame(string fileName, string? localPath)
    {
        var uploader = new ChatMediaUploader(_previewer, _frames);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadVideoAsync(uploader, fileName, localPath));

        Assert.Single(_fake.Uploads);
        Assert.Empty(_frames.Paths);
        Assert.Null(_fileState.File!.PreviewUploadId);
    }

    [Fact]
    public async Task VideoWhoseFrameCannotBeHad_GoesWithoutOne()
    {
        _frames.Result = null;
        var uploader = new ChatMediaUploader(_previewer, _frames);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadVideoAsync(uploader));

        Assert.Single(_fake.Uploads);
        Assert.Null(_fileState.File!.PreviewUploadId);
    }

    [Fact]
    public async Task VideoWhoseFrameTheServerRefuses_GoesWithoutOne()
    {
        var uploader = new ChatMediaUploader(_previewer, _frames);
        _fake.Results.Enqueue(MediaTransferResult.NoError);
        _fake.Results.Enqueue(MediaTransferResult.TooManyUploads);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadVideoAsync(uploader));

        Assert.Equal(2, _fake.Uploads.Count);
        Assert.Null(_fileState.File!.PreviewUploadId);
    }

    [Fact]
    public async Task VideoWhoseFrameIsCutOff_IsContinuedWithoutSendingItAgain()
    {
        var uploader = new ChatMediaUploader(_previewer, _frames);
        _fake.Results.Enqueue(MediaTransferResult.NoError);
        _fake.Results.Enqueue(MediaTransferResult.ConnectionFailed);

        Assert.Equal(ChatMediaUploadResult.ConnectionFailed, await UploadVideoAsync(uploader));
        Assert.Null(_fileState.File);

        Assert.Equal(ChatMediaUploadResult.NoError, await UploadVideoAsync(uploader));
        // The video stays where it was; only the frame goes on, and it was taken once
        Assert.Equal(3, _fake.Uploads.Count);
        Assert.Equal(MediaUploadKind.ImagePreview, _fake.Uploads[2].Kind);
        Assert.Equal("upload2", _fake.Uploads[2].ResumeUploadId);
        Assert.Single(_frames.Paths);
        Assert.Equal("upload2", _fileState.File!.PreviewUploadId);
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

            // The client reads the bytes before it sends them, and fails as the source does
            try
            {
                upload.OpenContent().GetAwaiter().GetResult().Dispose();
            }
            catch (Exception exception)
            {
                return Task.FromException<MediaUploadOutcome>(exception);
            }

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
