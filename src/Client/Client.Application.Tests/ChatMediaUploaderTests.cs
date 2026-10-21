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

    private Task<ChatImageUploadResult> UploadAsync(string fileName = "cat.png", IProgress<double>? progress = null) =>
        _uploader.UploadImageAsync(_session, 3,
            new ChatImageSource(fileName, Content.Length, () => Task.FromResult<Stream>(new MemoryStream(Content))),
            _state, progress, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Image_GoesAsItsReducedCopyAndThenItself()
    {
        var reported = new List<double>();

        var result = await UploadAsync(progress: new Collector(reported));

        Assert.Equal(ChatImageUploadResult.NoError, result);
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

        Assert.Equal(ChatImageUploadResult.NotAnImage, await UploadAsync());
        Assert.Empty(_fake.Uploads);
    }

    [Fact]
    public async Task ImageOfAFormatTheChatDoesNotCarry_IsNotUploaded()
    {
        _previewer.Result = _previewer.Result! with { MimeType = "image/bmp" };

        Assert.Equal(ChatImageUploadResult.NotAnImage, await UploadAsync());
        Assert.Empty(_fake.Uploads);
    }

    [Fact]
    public async Task InterruptedImage_IsContinuedByTheNextAttempt()
    {
        _fake.Results.Enqueue(MediaTransferResult.NoError);
        _fake.Results.Enqueue(MediaTransferResult.ConnectionFailed);

        Assert.Equal(ChatImageUploadResult.ConnectionFailed, await UploadAsync());
        Assert.Null(_state.Image);

        Assert.Equal(ChatImageUploadResult.NoError, await UploadAsync());

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

        Assert.Equal(ChatImageUploadResult.NoError, await UploadAsync());

        Assert.Null(_fake.Uploads[^1].ResumeUploadId);
        Assert.Equal("upload4", _state.Image!.UploadId);
    }

    [Fact]
    public async Task UploadedImage_IsNotUploadedAgain()
    {
        await UploadAsync();

        Assert.Equal(ChatImageUploadResult.NoError, await UploadAsync());
        Assert.Equal(2, _fake.Uploads.Count);
    }

    [Theory]
    [InlineData(MediaTransferResult.TooLarge, ChatImageUploadResult.TooLarge)]
    [InlineData(MediaTransferResult.NotAllowed, ChatImageUploadResult.NotAllowed)]
    [InlineData(MediaTransferResult.Unauthorized, ChatImageUploadResult.ConnectionFailed)]
    [InlineData(MediaTransferResult.StorageFull, ChatImageUploadResult.StorageFull)]
    [InlineData(MediaTransferResult.Canceled, ChatImageUploadResult.Canceled)]
    public async Task Refusal_IsPassedOn(MediaTransferResult refusal, ChatImageUploadResult expected)
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
