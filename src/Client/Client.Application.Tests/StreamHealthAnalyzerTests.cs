using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Shared.Models.Media;
using Xunit;

namespace Client.Application.Tests;

public class StreamHealthAnalyzerTests
{
    private const int UserId = 7;
    private static readonly TimeSpan Reading = TimeSpan.FromSeconds(2);

    private readonly StreamHealthAnalyzer _analyzer = new(UserId, TimeSpan.Zero);
    private TimeSpan _now;
    private long _frames;
    private long _dropped;

    [Fact]
    public void LossIsRaisedAtOnceAndClearedAfterTwoCleanReadings()
    {
        Read(Video(loss: 5));
        Assert.Equal([StreamIssueKind.PacketLoss], Kinds());
        Assert.Equal(StreamIssueSeverity.Warning, _analyzer.Issues[0].Severity);

        Read(Video());
        Assert.Equal([StreamIssueKind.PacketLoss], Kinds());

        Read(Video());
        Assert.Empty(_analyzer.Issues);
    }

    [Fact]
    public void HeavyLossIsCritical()
    {
        Read(Video(loss: 12));

        Assert.Equal(StreamIssueSeverity.Critical, Assert.Single(_analyzer.Issues).Severity);
    }

    [Fact]
    public void SilentStreamerIsNotBlamedRightAfterWatchingStarts()
    {
        Read(Sample());
        Assert.Empty(_analyzer.Issues);

        // Past the grace of the start, and silent since the previous reading
        Read(Sample());
        var issue = Assert.Single(_analyzer.Issues);
        Assert.Equal(StreamIssueKind.NoSourceFrames, issue.Kind);
        Assert.Equal(2, issue.Value);

        Read(Sample());
        Assert.Equal(4, Assert.Single(_analyzer.Issues).Value);
    }

    [Fact]
    public void MissingSoundCountsOnlyWhenTheScreenHasIt()
    {
        for (var i = 0; i < 3; i++)
            Read(Video());
        Assert.Empty(_analyzer.Issues);

        Read(Video(), isAudioExpected: true);
        Assert.Equal([StreamIssueKind.NoAudio], Kinds());
    }

    [Fact]
    public void PacketsWithoutFramesAfterAKeyframeRequestWaitForTheKeyframe()
    {
        for (var i = 0; i < 2; i++)
            Read(Video());

        Read(Video(keyframeRequests: 1), newFrames: 0);
        Assert.Equal([StreamIssueKind.WaitingForKeyframe], Kinds());

        // No new request, the old one is still being answered
        Read(Video(), newFrames: 0);
        Assert.Equal([StreamIssueKind.WaitingForKeyframe], Kinds());
    }

    [Fact]
    public void PacketsWithoutFramesOrRequestsStallTheDecoder()
    {
        for (var i = 0; i < 2; i++)
            Read(Video());

        Read(Video(), newFrames: 0);

        Assert.Equal([StreamIssueKind.DecoderStalled], Kinds());
    }

    [Fact]
    public void DroppedFramesMeanTheDrawingFallsBehind()
    {
        Read(Video());
        Read(Video(), newFrames: 60, newDropped: 30);

        var issue = Assert.Single(_analyzer.Issues);
        Assert.Equal(StreamIssueKind.RendererBehind, issue.Kind);
        Assert.Equal(StreamIssueSeverity.Critical, issue.Severity);
        Assert.Equal(15, issue.Value);
        Assert.Equal(30, issue.Total);
    }

    [Fact]
    public void DropsOfFramesThatCameInABunchAreTheNetworks()
    {
        Read(Video());
        Read(Video(), newFrames: 60, newDropped: 30, lateMs: 250);

        var issue = Assert.Single(_analyzer.Issues);
        Assert.Equal(StreamIssueKind.BurstyArrival, issue.Kind);
        Assert.Equal(StreamIssueArea.Network, issue.Area);
        Assert.Equal(250, issue.Value);
    }

    [Fact]
    public void FramesLateByLessThanAFewFramesLeaveTheDropsToTheRenderer()
    {
        Read(Video());
        // 30 frames a second: 100 ms is three frames
        Read(Video(), newFrames: 60, newDropped: 30, lateMs: 100);

        Assert.Equal([StreamIssueKind.RendererBehind], Kinds());
    }

    [Fact]
    public void OtherUsersStreamsAreIgnored()
    {
        Read(Sample(new InboundStreamSample(UserId + 1, MediaSourceType.Screen, MediaType.Video, 100, 30, 0, 0),
            new InboundStreamSample(UserId, MediaSourceType.Screen, MediaType.Video, 100, 0, 0, 0)));

        Assert.Empty(_analyzer.Issues);
    }

    private void Read(TransportStatsSample sample, bool isAudioExpected = false, long newFrames = 60, long newDropped = 0,
        double lateMs = 0)
    {
        _now += Reading;
        _frames += newFrames;
        _dropped += newDropped;

        _analyzer.Update(sample, new FrameStats(_frames, _frames - _dropped, _dropped, 0, 0, lateMs), isAudioExpected,
            _now);
    }

    private StreamIssueKind[] Kinds() => _analyzer.Issues.Select(issue => issue.Kind).ToArray();

    private static TransportStatsSample Video(double loss = 0, uint keyframeRequests = 0) =>
        Sample(new InboundStreamSample(UserId, MediaSourceType.Screen, MediaType.Video, 100, loss, 5, keyframeRequests));

    private static TransportStatsSample Sample(params InboundStreamSample[] streams) => new(null, 0, 0, streams);
}
