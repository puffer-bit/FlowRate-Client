using System.Reactive.Linq;
using Client.Application.Chat;
using Client.Application.Ports;
using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatVideoAttachmentTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("flowrate-video-attachment-").FullName;

    private sealed class FakeVideoPreparer(string folder) : IVideoPreparer
    {
        public List<VideoPreparation> Asked { get; } = [];
        public List<string> Written { get; } = [];
        public bool IsAvailable { get; init; } = true;
        public bool Fails { get; init; }

        public Task<PreparedVideo?> PrepareAsync(ChatMediaSource source, VideoPreparation preparation,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            lock (Asked)
                Asked.Add(preparation);
            if (Fails)
                throw new InvalidOperationException("No decoder");
            var facts = new VideoFacts("h264", 1920, 1080, 30, "aac", TimeSpan.FromSeconds(10), true, false, source.Size);
            var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".mp4");
            File.WriteAllBytes(path, new byte[100]);
            lock (Written)
                Written.Add(path);
            return Task.FromResult<PreparedVideo?>(new PreparedVideo(facts, VideoPreparationKind.Transcode, path,
                Path.ChangeExtension(source.FileName, ".mp4"), 100));
        }
    }

    private static PickedFile Picked(string name) =>
        new(name, null, 5000, () => Task.FromResult<Stream>(new MemoryStream(new byte[5000])));

    private static ChatAttachmentVM Attachment(string name, IVideoPreparer preparer, VideoCompressionPolicy? policy = null,
        ChatAttachmentPreferences? preferences = null) =>
        new(Picked(name), false, null!, null, preferences, null, preparer, policy);

    [Fact]
    public async Task Video_GoesAsTheFileItWasCompressedTo()
    {
        var preparer = new FakeVideoPreparer(_folder);
        using var attachment = Attachment("clip.mov", preparer);

        var source = await attachment.GetUploadSourceAsync(TestContext.Current.CancellationToken);

        Assert.True(attachment.IsVideo);
        Assert.True(attachment.IsFile);
        Assert.Equal("clip.mp4", source.FileName);
        Assert.Equal(100, source.Size);
        Assert.Equal(preparer.Written[^1], source.LocalPath);
        var asked = Assert.Single(preparer.Asked);
        Assert.True(asked.Compress);
        Assert.Equal(VideoCompressionPolicy.Default.MaxSide, asked.Preset.ShortSide);
    }

    [Fact]
    public async Task ChoosingAnotherResolution_CompressesAgain_AndDeletesTheFormerFile()
    {
        var preparer = new FakeVideoPreparer(_folder);
        var attachment = Attachment("clip.mp4", preparer);
        await attachment.GetUploadSourceAsync(TestContext.Current.CancellationToken);
        var first = preparer.Written[^1];

        await attachment.ChooseVideoSideCommand.Execute(480);
        var source = await attachment.GetUploadSourceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(480, preparer.Asked[^1].Preset.ShortSide);
        Assert.NotEqual(first, source.LocalPath);
        await Until(() => !File.Exists(first));

        attachment.Dispose();
        await Until(() => !File.Exists(source.LocalPath!));
    }

    [Fact]
    public async Task VideoThatCannotBeCompressed_GoesAsItIs()
    {
        using var attachment = Attachment("clip.webm", new FakeVideoPreparer(_folder) { Fails = true });

        var source = await attachment.GetUploadSourceAsync(TestContext.Current.CancellationToken);

        Assert.Same(attachment.Source, source);
    }

    [Fact]
    public void ServerThatTakesOnlyCompressedVideos_KeepsCompressionOn_AndOffersNoLargerSide()
    {
        using var attachment = Attachment("clip.mp4", new FakeVideoPreparer(_folder), new VideoCompressionPolicy(720, false),
            new ChatAttachmentPreferences(CompressVideos: false, VideoSide: 1080));

        Assert.True(attachment.IsCompressed);
        Assert.True(attachment.IsVideoCompressionForced);
        Assert.False(attachment.CanToggleCompression);
        Assert.Equal([480, 720], attachment.VideoSides.Select(option => option.Side));
        Assert.Equal(720, attachment.VideoSide);
    }

    [Fact]
    public void WithoutAPreparer_AVideoIsAPlainFile()
    {
        using var attachment = Attachment("clip.mp4", new FakeVideoPreparer(_folder) { IsAvailable = false });

        Assert.False(attachment.IsVideo);
        Assert.False(attachment.HasMediaOptions);
        Assert.False(attachment.CanToggleCompression);
    }

    [Fact]
    public void Policy_OffersTheUsualSidesAndItsOwn()
    {
        Assert.Equal([480, 720, 1080], new VideoCompressionPolicy(720, true).Sides);
        Assert.Equal([360, 480], new VideoCompressionPolicy(360, true).Sides.Where(side => side <= 480));
        Assert.Equal([360], new VideoCompressionPolicy(360, false).Sides);
        Assert.Equal(720, new VideoCompressionPolicy(720, false).SideFor(1080));
        Assert.Equal(VideoCompressionPolicy.Default, VideoCompressionPolicy.From(0, false));
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    public void Dispose() => Directory.Delete(_folder, true);
}
