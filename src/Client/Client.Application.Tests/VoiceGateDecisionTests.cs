using Client.Core.Media;
using Xunit;

namespace Client.Application.Tests;

public class VoiceGateDecisionTests
{
    private const double Threshold = -40;

    // The level element of the transport reports every 10 ms
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);

    private TimeSpan _now = TimeSpan.FromSeconds(1);

    private VoiceGateDecision CreateGate() => new(Threshold);

    // Feeds the same peak for a while, one reading per interval, and tells when the gate last changed
    private TimeSpan? Feed(VoiceGateDecision gate, double peakDb, TimeSpan duration)
    {
        TimeSpan? changedAt = null;

        for (var end = _now + duration; _now < end; _now += Interval)
        {
            if (gate.OnLevel(peakDb, _now))
                changedAt = _now;
        }

        return changedAt;
    }

    [Fact]
    public void OpensAtTheThresholdAndNotBelow()
    {
        var gate = CreateGate();

        Feed(gate, Threshold - 0.5, TimeSpan.FromMilliseconds(200));
        Assert.False(gate.IsOpen);

        Assert.True(gate.OnLevel(Threshold, _now));
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void OpenGateKeepsReadingsWithinTheHysteresis()
    {
        var gate = CreateGate();

        Feed(gate, -20, TimeSpan.FromMilliseconds(100));
        Feed(gate, Threshold - VoiceGateDecision.HysteresisDb + 1, TimeSpan.FromSeconds(2));

        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void SilenceReadsAsTheFloor()
    {
        var gate = CreateGate();

        Assert.False(gate.OnLevel(double.NegativeInfinity, _now));
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void ShortBurstClosesAfterTheShortestHold()
    {
        var gate = CreateGate();

        // A knock: one loud interval
        Feed(gate, -10, Interval);
        var start = _now - Interval;
        var closedAt = Feed(gate, -80, TimeSpan.FromSeconds(1));

        Assert.False(gate.IsOpen);
        Assert.NotNull(closedAt);

        var held = closedAt.Value - start;
        Assert.InRange(held, VoiceGateDecision.MinHoldTime, VoiceGateDecision.MinHoldTime + 2 * Interval);
    }

    [Fact]
    public void SustainedSpeechHoldsTheLongestBetweenWords()
    {
        var gate = CreateGate();

        Feed(gate, -20, TimeSpan.FromSeconds(2));
        var lastLoud = _now - Interval;
        var closedAt = Feed(gate, -80, TimeSpan.FromSeconds(1));

        Assert.NotNull(closedAt);

        var held = closedAt.Value - lastLoud;
        Assert.InRange(held, VoiceGateDecision.MaxHoldTime, VoiceGateDecision.MaxHoldTime + Interval);
    }

    [Fact]
    public void PauseShorterThanTheHoldKeepsASentenceInOnePiece()
    {
        var gate = CreateGate();
        var openings = 0;

        for (var word = 0; word < 5; word++)
        {
            if (Feed(gate, -20, TimeSpan.FromMilliseconds(400)) != null)
                openings++;

            Assert.Null(Feed(gate, -80, TimeSpan.FromMilliseconds(250)));
        }

        Assert.Equal(1, openings);
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void HoldGrowsWithTheSpokenTime()
    {
        Assert.Equal(VoiceGateDecision.MinHoldTime, VoiceGateDecision.HoldTimeFor(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(200), VoiceGateDecision.HoldTimeFor(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(VoiceGateDecision.MaxHoldTime, VoiceGateDecision.HoldTimeFor(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void RaisedThresholdWaitsOutTheHold()
    {
        var gate = CreateGate();

        Feed(gate, -20, TimeSpan.FromSeconds(1));
        gate.SetThreshold(-5, _now, isOpen: true);

        Assert.False(gate.OnLevel(-20, _now + Interval));
        Assert.True(gate.IsOpen);

        _now += Interval;
        Assert.NotNull(Feed(gate, -20, TimeSpan.FromSeconds(1)));
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void GateTurnedOnAgainWhileOpenClosesAfterTheHold()
    {
        var gate = CreateGate();

        gate.SetThreshold(Threshold, _now, isOpen: true);

        Assert.True(gate.IsOpen);
        Assert.NotNull(Feed(gate, -80, TimeSpan.FromSeconds(1)));
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void SummaryCountsTheOpenShareAndResets()
    {
        var gate = CreateGate();

        Feed(gate, -20, TimeSpan.FromMilliseconds(500));
        Feed(gate, -80, TimeSpan.FromMilliseconds(1500));

        var summary = gate.TakeSummary();

        Assert.NotNull(summary);
        Assert.Equal(Threshold, summary.ThresholdDb);
        Assert.Equal(1, summary.Openings);
        Assert.Equal(-20, summary.LoudestDb);
        Assert.InRange(summary.OpenPercent, 40, 50);
        Assert.Null(gate.TakeSummary());
    }
}
