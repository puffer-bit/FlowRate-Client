using System.Buffers.Binary;
using System.Text;
using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public class VideoCompressionTests
{
    private static readonly VideoCompressionPreset Medium = VideoCompressionPreset.Medium;

    private static readonly VideoFacts Small =
        new("h264", 1280, 720, 30, "aac", TimeSpan.FromSeconds(10), true, true, 3_000_000);

    [Theory]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(1080, 1920, 720, 1280)]
    [InlineData(2560, 1080, 1280, 540)]
    [InlineData(640, 480, 640, 480)]
    [InlineData(2940, 1912, 1106, 720)]
    [InlineData(1001, 563, 1000, 562)]
    public void Fit_ReducesToThePresetInEvenSizes(int width, int height, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), Medium.Fit(width, height));

    [Fact]
    public void FrameRate_IsBoundedAndBitrateFollowsIt()
    {
        Assert.Equal(30, VideoCompressionPreset.Low.FrameRateFor(60));
        Assert.Equal(60, Medium.FrameRateFor(59.94));
        Assert.Equal(30, Medium.FrameRateFor(0));
        Assert.Equal(Medium.VideoKbps, Medium.VideoKbpsAt(30));
        Assert.Equal(Medium.VideoKbps * 3 / 2, Medium.VideoKbpsAt(60));
    }

    [Fact]
    public void Decide_SendsAFittingVideoAsItIs() =>
        Assert.Equal(VideoPreparationKind.AsIs, VideoPreparationRules.Decide(Small, true, false, Medium));

    [Fact]
    public void Decide_RemuxesForMetadataOrLayout()
    {
        Assert.Equal(VideoPreparationKind.Remux, VideoPreparationRules.Decide(Small, true, true, Medium));
        Assert.Equal(VideoPreparationKind.Remux, VideoPreparationRules.Decide(Small with { IsFastStart = false }, false, false, Medium));
        Assert.Equal(VideoPreparationKind.Remux, VideoPreparationRules.Decide(Small with { VideoCodec = "h265" }, false, true, Medium));
    }

    [Fact]
    public void Decide_EncodesWhatIsAboveThePreset()
    {
        Assert.Equal(VideoPreparationKind.Transcode, VideoPreparationRules.Decide(Small with { Width = 1920, Height = 1080 }, true, false, Medium));
        Assert.Equal(VideoPreparationKind.Transcode, VideoPreparationRules.Decide(Small with { Size = 30_000_000 }, true, false, Medium));
        Assert.Equal(VideoPreparationKind.Transcode, VideoPreparationRules.Decide(Small with { VideoCodec = "h265" }, true, false, Medium));
        Assert.Equal(VideoPreparationKind.Transcode, VideoPreparationRules.Decide(Small with { AudioCodec = "pcm" }, true, false, Medium));
        Assert.Equal(VideoPreparationKind.Transcode, VideoPreparationRules.Decide(Small with { FrameRate = 120 }, true, false, Medium));
    }

    [Fact]
    public void Decide_LeavesOtherContainersAloneUnlessCompressed()
    {
        var webm = Small with { VideoCodec = "vp8", AudioCodec = "vorbis", IsMp4 = false, IsFastStart = false };
        Assert.Equal(VideoPreparationKind.AsIs, VideoPreparationRules.Decide(webm, false, true, Medium));
        Assert.Equal(VideoPreparationKind.Transcode, VideoPreparationRules.Decide(webm, true, true, Medium));
        Assert.Equal(VideoPreparationKind.Remux,
            VideoPreparationRules.Decide(Small with { IsMp4 = false, IsFastStart = false }, true, false, Medium));
    }

    [Theory]
    [InlineData(new[] { "ftyp", "moov", "mdat" }, true, true)]
    [InlineData(new[] { "ftyp", "wide", "mdat", "moov" }, true, false)]
    [InlineData(new[] { "ftyp", "mdat" }, false, false)]
    [InlineData(new[] { "RIFF" }, false, false)]
    public void Mp4Layout_FindsWhereTheIndexIs(string[] boxes, bool isMp4, bool isFastStart)
    {
        using var stream = new MemoryStream();
        foreach (var box in boxes)
        {
            var bytes = new byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
            Encoding.ASCII.GetBytes(box).CopyTo(bytes, 4);
            stream.Write(bytes);
        }

        Assert.Equal(new Mp4Layout(isMp4, isFastStart), Mp4Layout.Read(stream));
    }

    [Fact]
    public void Mp4Layout_ReadsLargeSizes()
    {
        using var stream = new MemoryStream();
        var mdat = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(mdat, 1);
        "mdat"u8.CopyTo(mdat.AsSpan(4));
        BinaryPrimitives.WriteUInt64BigEndian(mdat.AsSpan(8), 24);
        stream.Write(mdat);
        var moov = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(moov, 8);
        "moov"u8.CopyTo(moov.AsSpan(4));
        stream.Write(moov);

        Assert.Equal(new Mp4Layout(true, false), Mp4Layout.Read(stream));
    }
}
