using Client.Core.Media;
using Client.Core.Settings;
using Xunit;

namespace Client.Application.Tests;

public class VoiceGateDecisionTests
{
    private const double Threshold = -40;

    // The level element of the transport reports every 10 ms
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);

    private TimeSpan _now = TimeSpan.FromSeconds(1);

    private static VoiceGateDecision CreateGate(bool isAdaptive = false) =>
        new(new VoiceGateOptions(Threshold, IsAdaptiveThresholdEnabled: isAdaptive));

    // A steady noise: peaks that wander a few decibels around a level, as those of a fan do
    private TimeSpan? FeedNoise(VoiceGateDecision gate, double levelDb, TimeSpan duration)
    {
        TimeSpan? changedAt = null;
        var random = new Random(7);

        for (var end = _now + duration; _now < end; _now += Interval)
        {
            if (gate.OnLevel(levelDb + random.NextDouble() * 6 - 3, _now))
                changedAt = _now;
        }

        return changedAt;
    }

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
        gate.SetOptions(new VoiceGateOptions(-5), _now, isOpen: true);

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

        gate.SetOptions(new VoiceGateOptions(Threshold), _now, isOpen: true);

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

    [Fact]
    public void AdaptiveThresholdStartsAtTheOneSet()
    {
        var gate = CreateGate(isAdaptive: true);

        // A first reading that is loud but stays below the threshold must not raise it
        gate.OnLevel(Threshold - 1, _now);

        Assert.Equal(Threshold - VoiceGateDecision.NoiseMarginDb, gate.NoiseFloorDb);
        Assert.Equal(Threshold, gate.EffectiveThresholdDb);
    }

    [Fact]
    public void AdaptiveThresholdRisesAboveASteadyNoise()
    {
        var gate = CreateGate(isAdaptive: true);

        // A fan around the threshold, whose louder peaks open a plain gate. The adaptive one opens on
        // them too until it has learned the room.
        FeedNoise(gate, Threshold - 2, TimeSpan.FromSeconds(60));
        gate.TakeSummary();
        FeedNoise(gate, Threshold - 2, TimeSpan.FromSeconds(10));

        var summary = gate.TakeSummary()!;
        Assert.Equal(0, summary.Openings);
        Assert.Equal(0, summary.OpenPercent);
        Assert.InRange(gate.NoiseFloorDb!.Value, Threshold - 6, Threshold - 2);
        Assert.InRange(gate.EffectiveThresholdDb, Threshold + 6, Threshold + 10);

        // A voice well above the noise still opens it
        Assert.True(gate.OnLevel(-20, _now));
    }

    [Fact]
    public void PlainGateIsHeldOpenByTheSameNoise()
    {
        var gate = CreateGate();

        FeedNoise(gate, Threshold - 2, TimeSpan.FromSeconds(60));
        gate.TakeSummary();
        FeedNoise(gate, Threshold - 2, TimeSpan.FromSeconds(10));

        Assert.Equal(100, gate.TakeSummary()!.OpenPercent);
        Assert.Null(gate.NoiseFloorDb);
    }

    [Fact]
    public void AdaptiveThresholdIsCapped()
    {
        var gate = CreateGate(isAdaptive: true);

        // A noise that grows louder and louder, which the threshold follows up to the cap
        for (var level = Threshold - 9; level < -5; level += 0.5)
            FeedNoise(gate, level, TimeSpan.FromSeconds(1));

        Assert.Equal(Threshold + VoiceGateDecision.MaxAdaptiveRiseDb, gate.EffectiveThresholdDb);
    }

    [Fact]
    public void AdaptiveFloorIsRobustToSpeech()
    {
        var gate = CreateGate(isAdaptive: true);

        FeedNoise(gate, -70, TimeSpan.FromSeconds(10));
        var quiet = gate.NoiseFloorDb!.Value;

        // Quiet words below the threshold, between pauses of the room
        for (var word = 0; word < 5; word++)
        {
            Feed(gate, Threshold - 2, TimeSpan.FromMilliseconds(300));
            FeedNoise(gate, -70, TimeSpan.FromMilliseconds(500));
        }

        Assert.False(gate.IsOpen);
        Assert.InRange(gate.NoiseFloorDb!.Value, quiet - 3, quiet + 3);
        Assert.Equal(Threshold, gate.EffectiveThresholdDb);
    }

    [Fact]
    public void AdaptiveFloorFallsQuicklyWhenTheRoomGetsQuieter()
    {
        var gate = CreateGate(isAdaptive: true);

        FeedNoise(gate, Threshold - 2, TimeSpan.FromSeconds(60));
        Assert.True(gate.EffectiveThresholdDb > Threshold);

        Feed(gate, -90, TimeSpan.FromSeconds(2));

        Assert.Equal(Threshold, gate.EffectiveThresholdDb);
    }

    [Fact]
    public void AdaptiveFloorOnlyCreepsUpWhileTheGateIsOpen()
    {
        var gate = CreateGate(isAdaptive: true);

        FeedNoise(gate, -70, TimeSpan.FromSeconds(5));
        var floor = gate.NoiseFloorDb!.Value;

        // Ten seconds of speech without a single pause
        Feed(gate, -20, TimeSpan.FromSeconds(10));

        Assert.True(gate.IsOpen);
        Assert.InRange(gate.NoiseFloorDb!.Value, floor + 4, floor + 5.5);
        Assert.Equal(Threshold, gate.EffectiveThresholdDb);
    }

    [Fact]
    public void SummaryReportsTheFloorAndTheEffectiveThreshold()
    {
        var gate = CreateGate(isAdaptive: true);

        FeedNoise(gate, Threshold - 2, TimeSpan.FromSeconds(30));
        var summary = gate.TakeSummary()!;

        Assert.Equal(Threshold, summary.ThresholdDb);
        Assert.Equal(gate.EffectiveThresholdDb, summary.EffectiveThresholdDb);
        Assert.Equal(gate.NoiseFloorDb, summary.NoiseFloorDb);
    }

    // Readings of the gate level and of the voice detector, which reports VoiceLag later
    private static readonly TimeSpan VoiceLag = TimeSpan.FromMilliseconds(20);

    private static VoiceGateDecision CreateDetectingGate(bool isEnabled = true, bool hasDetector = true) =>
        new(new VoiceGateOptions(Threshold, IsVoiceDetectionEnabled: isEnabled), hasDetector);

    // A sound for a while, which the detector does or does not take for a voice; tells when the gate last changed
    private TimeSpan? FeedSound(VoiceGateDecision gate, double peakDb, bool isVoice, TimeSpan duration)
    {
        TimeSpan? changedAt = null;

        for (var end = _now + duration; _now < end; _now += Interval)
        {
            if (gate.OnLevel(peakDb, _now))
                changedAt = _now;

            // The detector silences what it takes for no voice
            if (gate.OnVoice(isVoice ? peakDb : double.NegativeInfinity, _now + VoiceLag))
                changedAt = _now + VoiceLag;
        }

        return changedAt;
    }

    [Fact]
    public void LoudSoundThatIsNoVoiceKeepsTheGateClosed()
    {
        var gate = CreateDetectingGate();

        // Typing: loud clicks with the room between them
        for (var key = 0; key < 20; key++)
        {
            Assert.Null(FeedSound(gate, -10, isVoice: false, Interval));
            Assert.Null(FeedSound(gate, -80, isVoice: false, TimeSpan.FromMilliseconds(150)));
        }

        Assert.False(gate.IsOpen);
        Assert.Equal(0, gate.TakeSummary()!.Openings);
    }

    [Fact]
    public void VoiceOpensTheGateOnceTheDetectorHearsIt()
    {
        var gate = CreateDetectingGate();
        var start = _now;

        var openedAt = FeedSound(gate, -20, isVoice: true, TimeSpan.FromMilliseconds(100));

        Assert.True(gate.IsOpen);
        Assert.Equal(start + VoiceLag, openedAt);
    }

    [Fact]
    public void QuietVoiceKeepsTheGateClosed()
    {
        var gate = CreateDetectingGate();

        // The detector hears a voice, but below the threshold: someone talking in the next room
        Assert.Null(FeedSound(gate, Threshold - 10, isVoice: true, TimeSpan.FromSeconds(1)));
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void ClickLongBeforeAVoiceDoesNotCountWithIt()
    {
        var gate = CreateDetectingGate();

        gate.OnLevel(-10, _now);
        gate.OnVoice(-60, _now + VoiceGateDecision.VoiceWindow + Interval);

        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void GateClosesAfterTheHoldWhenTheVoiceStops()
    {
        var gate = CreateDetectingGate();

        FeedSound(gate, -20, isVoice: true, TimeSpan.FromSeconds(2));
        var lastVoice = _now - Interval + VoiceLag;

        // Typing right after the sentence does not keep the gate open; it counts with the voice only
        // within the window
        var closedAt = FeedSound(gate, -10, isVoice: false, TimeSpan.FromSeconds(1));

        Assert.False(gate.IsOpen);
        Assert.NotNull(closedAt);
        Assert.InRange(closedAt.Value - lastVoice, VoiceGateDecision.MaxHoldTime,
            VoiceGateDecision.MaxHoldTime + VoiceGateDecision.VoiceWindow + Interval);
    }

    [Fact]
    public void GateWithoutADetectorGoesByTheLevelAlone()
    {
        var gate = CreateDetectingGate(hasDetector: false);

        Assert.False(gate.IsVoiceDetectionActive);
        Assert.NotNull(FeedSound(gate, -10, isVoice: false, Interval));
        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void VoiceDetectionTurnedOffGoesByTheLevelAlone()
    {
        var gate = CreateDetectingGate(isEnabled: false);

        FeedSound(gate, -10, isVoice: false, Interval);
        Assert.True(gate.IsOpen);
        Assert.Null(gate.TakeSummary()!.VoicePercent);
    }

    [Fact]
    public void VoiceDetectionTurnedOnLaterAppliesToTheNextSound()
    {
        var gate = CreateDetectingGate(isEnabled: false);

        gate.SetOptions(new VoiceGateOptions(Threshold, IsVoiceDetectionEnabled: true), _now, isOpen: false);

        Assert.Null(FeedSound(gate, -10, isVoice: false, TimeSpan.FromMilliseconds(200)));
        Assert.NotNull(FeedSound(gate, -20, isVoice: true, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void SummaryReportsHowMuchOfTheTimeAVoiceWasHeard()
    {
        var gate = CreateDetectingGate();

        FeedSound(gate, -20, isVoice: true, TimeSpan.FromSeconds(1));
        FeedSound(gate, -10, isVoice: false, TimeSpan.FromSeconds(3));

        Assert.Equal(25, gate.TakeSummary()!.VoicePercent);
    }

    [Fact]
    public void AdaptiveThresholdLearnsANoiseTheDetectorKeepsOut()
    {
        var gate = new VoiceGateDecision(new VoiceGateOptions(Threshold, true, true), hasVoiceDetector: true);

        // A loud fan never opens a detecting gate, so the closed gate learns it
        FeedSound(gate, -30, isVoice: false, TimeSpan.FromSeconds(30));

        Assert.False(gate.IsOpen);
        Assert.InRange(gate.NoiseFloorDb!.Value, -31, -29);
        Assert.Equal(Threshold + VoiceGateDecision.MaxAdaptiveRiseDb, gate.EffectiveThresholdDb);
    }
}
