using System.Diagnostics;
using Client.Core.Media;
using Xunit;

namespace Client.Application.Tests;

public class FramePacerTests
{
    private const long Frame144 = 1_000_000_000 / 144;

    private readonly List<string> _released = [];

    private FramePacer<string> CreatePacer(bool isTimed = true, int bufferFrames = 2) =>
        new(_released.Add) { IsTimed = isTimed, BufferFrames = bufferFrames };

    [Fact]
    public void UntimedShowsTheNewestAndReleasesTheRest()
    {
        using var pacer = CreatePacer(isTimed: false);

        pacer.Enqueue("a", 0);
        pacer.Enqueue("b", Frame144);
        pacer.Enqueue("c", 2 * Frame144);

        Assert.Equal("c", pacer.Pick());
        Assert.Null(pacer.Pick());
        Assert.Equal(["a", "b"], _released);

        var stats = pacer.Stats;
        Assert.Equal(3, stats.Received);
        Assert.Equal(1, stats.Shown);
        Assert.Equal(2, stats.Dropped);
    }

    [Fact]
    public void FramesWithoutTimestampsAreShownAsTheyCome()
    {
        using var pacer = CreatePacer();

        pacer.Enqueue("a", -1);
        Assert.Equal("a", pacer.Pick());

        pacer.Enqueue("b", -1);
        Assert.Equal("b", pacer.Pick());
        Assert.Empty(_released);
    }

    [Fact]
    public void TimedFrameWaitsForItsPlace()
    {
        // A whole second of room: nothing that just arrived is due yet
        using var pacer = CreatePacer(bufferFrames: 4);

        pacer.Enqueue("a", 0);
        pacer.Enqueue("b", 250_000_000);

        Assert.Null(pacer.Pick());
        Assert.True(pacer.HasPending);
    }

    [Fact]
    public void TimedFramesComeOutInOrderOnceDue()
    {
        using var pacer = CreatePacer(bufferFrames: 1);

        // Frames that arrive as fast as their timestamps go are due one interval after arriving
        for (var index = 0; index < 4; index++)
        {
            pacer.Enqueue($"f{index}", index * Frame144);
            SpinFor(Frame144);
        }

        SpinFor(3 * Frame144);

        var shown = new List<string>();
        while (pacer.Pick() is { } frame)
            shown.Add(frame);

        // Four due at once: the two oldest are dropped, the other two shown on consecutive picks
        Assert.Equal(["f2", "f3"], shown);
        Assert.Equal(4, pacer.Stats.Received);
        Assert.Equal(pacer.Stats.Received, pacer.Stats.Shown + pacer.Stats.Dropped);
    }

    [Fact]
    public void TwoDueFramesAreBothShownOneAfterTheOther()
    {
        using var pacer = CreatePacer(bufferFrames: 1);

        pacer.Enqueue("a", 0);
        SpinFor(Frame144);
        pacer.Enqueue("b", Frame144);

        // A refresh that came late finds both due
        SpinFor(3 * Frame144);

        Assert.Equal("a", pacer.Pick());
        Assert.Equal("b", pacer.Pick());
        Assert.Equal(0, pacer.Stats.Dropped);
    }

    [Fact]
    public void QueueCannotGrowBeyondTheBuffer()
    {
        using var pacer = CreatePacer(bufferFrames: 2);

        // A burst far ahead of the clock
        for (var index = 0; index < 20; index++)
            pacer.Enqueue($"f{index}", index * Frame144);

        // The buffer and one frame of slack are kept, the oldest are given back at once
        Assert.Equal(17, _released.Count);
        Assert.Equal("f0", _released[0]);
    }

    [Fact]
    public void JumpBackStartsTheTimingOver()
    {
        using var pacer = CreatePacer(bufferFrames: 1);

        pacer.Enqueue("a", 5_000_000_000);
        SpinFor(2 * Frame144);
        Assert.Equal("a", pacer.Pick());

        // A new stream starts from zero; it must not wait five seconds
        pacer.Enqueue("b", 0);
        SpinFor(2 * Frame144);
        Assert.Equal("b", pacer.Pick());
    }

    [Fact]
    public void ResetGivesBackWhatWaits()
    {
        var pacer = CreatePacer(bufferFrames: 4);

        pacer.Enqueue("a", 0);
        pacer.Enqueue("b", Frame144);
        pacer.Reset();

        Assert.Equal(["a", "b"], _released);
        Assert.False(pacer.HasPending);
    }

    private static void SpinFor(long nanoseconds)
    {
        var until = Stopwatch.GetTimestamp() + (long)(nanoseconds * (Stopwatch.Frequency / 1e9));

        while (Stopwatch.GetTimestamp() < until)
            Thread.SpinWait(50);
    }
}
