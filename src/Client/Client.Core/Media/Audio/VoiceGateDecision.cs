using Client.Core.Settings;

namespace Client.Core.Media;

/// <summary>
/// What a voice gate did over a period, for the log.
/// </summary>
/// <param name="ThresholdDb">The level the gate opens from, as set.</param>
/// <param name="EffectiveThresholdDb">The level it opens from at the end of the period, raised above the noise.</param>
/// <param name="NoiseFloorDb">The noise of the room it measured, or null without the adaptive threshold.</param>
/// <param name="OpenPercent">The share of the period the gate was open.</param>
/// <param name="Openings">How many times it opened.</param>
/// <param name="LoudestDb">The loudest peak of the period.</param>
/// <param name="AverageOpenPeakDb">The average peak while the gate was open.</param>
/// <param name="VoicePercent">
/// The share of the period the voice detector heard a voice, or null without voice detection.
/// </param>
public sealed record VoiceGateSummary(double ThresholdDb, double EffectiveThresholdDb, double? NoiseFloorDb,
    double OpenPercent, int Openings, double LoudestDb, double AverageOpenPeakDb, double? VoicePercent);

/// <summary>
/// Decides from the measured loudness, and from a voice detector where there is one, whether the
/// voice gate of a microphone is open. It knows nothing of how either is measured: every reading
/// comes with the time it was taken, so the same logic runs in the transport, in the microphone
/// test and in the tests.
/// </summary>
/// <remarks>
/// <para>The gate opens at the threshold and stays open down to <see cref="HysteresisDb"/> below it,
/// so a word that fades out does not switch it on and off around a single value.</para>
/// <para>After the last loud reading it holds for a while, which covers the pauses between words. The
/// hold grows with how long the gate has been speaking: a knock or a cough that opened it is cut
/// short after <see cref="MinHoldTime"/>, while a sentence keeps up to <see cref="MaxHoldTime"/>
/// between its words, so its end is not chopped.</para>
/// <para>With the adaptive threshold the gate follows the noise of the room while it is closed and
/// opens only <see cref="NoiseMarginDb"/> above it. The follower falls fast and rises slowly: it
/// settles near the quiet end of the noise, a voice or a knock pulls it up by a fraction of a
/// decibel, and a room that gets quieter is followed down within a second. While the gate is open
/// the follower may still fall, and rise at half the speed: a noise loud enough to open the gate
/// before it was learned would otherwise hold it open through the hysteresis for good, never to be
/// heard by a closed gate. It never goes below the threshold of the user and never more
/// than <see cref="MaxAdaptiveRiseDb"/> above it, so a long loud noise cannot shut the gate.</para>
/// <para>With voice detection the gate opens, and holds, only while the readings are loud enough
/// and the detector hears a voice, so a key or a knock as loud as the voice stays out. The two come
/// from separate measurements, and the detector reports later than the loudness, so each counts for
/// <see cref="VoiceWindow"/> after it was taken: the gate opens on whichever of the two arrives
/// second. A gate without a detector, or with voice detection off, goes by the loudness alone.</para>
/// <para>Not thread-safe: the caller runs every call under one lock.</para>
/// </remarks>
public sealed class VoiceGateDecision
{
    /// <summary>
    /// How far below the threshold an open gate still counts a reading as loud.
    /// </summary>
    public const double HysteresisDb = 6;

    /// <summary>
    /// The hold after a burst that opened the gate for a moment only. Long enough for the gap
    /// between two syllables, short enough that a knock does not carry the room after it.
    /// </summary>
    public static readonly TimeSpan MinHoldTime = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The hold once the gate has been speaking for a while: the pause between the words of a
    /// sentence and a breath, the 300 ms the gate always held before, and a little more.
    /// </summary>
    public static readonly TimeSpan MaxHoldTime = TimeSpan.FromMilliseconds(400);

    // Every second the gate has been speaking adds half a second to the hold, so the full hold is
    // reached after 600 ms, about a short word
    private const double HoldGrowth = 0.5;

    /// <summary>
    /// Silence reads as minus infinity; anything below this counts as this.
    /// </summary>
    public const double AudioFloorDb = -100;

    /// <summary>
    /// How far above the noise of the room the adaptive threshold opens. The peaks of a steady
    /// noise spread over about 6 dB from one 10 ms interval to the next, and the follower sits near
    /// their quiet end; this keeps the louder ones below where an open gate closes, which is
    /// <see cref="HysteresisDb"/> lower still. Speech is 20 dB and more above the room.
    /// </summary>
    public const double NoiseMarginDb = 12;

    /// <summary>
    /// How far the adaptive threshold may rise above the one the user set.
    /// </summary>
    public const double MaxAdaptiveRiseDb = 15;

    // The follower settles where the noise is below it r / (r + f) of the time, here a tenth: near the
    // quiet end. Rising by 2 dB a second, a fan that starts is caught up with in a few seconds, while a
    // word below the threshold moves it by less than a decibel.
    private const double FloorRiseDbPerSecond = 2;
    private const double FloorFallDbPerSecond = 18;

    // An open gate hears mostly the voice, but speech pauses a tenth of the time and more, and at
    // this speed every pause pulls the follower back down to the room: it settles where the
    // readings are below it a nineteenth of the time. A noise that held the gate open from the
    // start is learned in about ten seconds.
    private const double OpenFloorRiseDbPerSecond = FloorRiseDbPerSecond / 2;

    /// <summary>
    /// How far apart a loud reading and a voice reading may be and still count together. A
    /// detector such as RNNoise hears most words within 10 ms of the level and a soft onset up to
    /// 70 ms later, and its messages come apart from those of the level; a word that the detector
    /// is late for still opens the gate with its next loud readings. Much more would let a click
    /// count with a voice that came well after it.
    /// </summary>
    public static readonly TimeSpan VoiceWindow = TimeSpan.FromMilliseconds(50);

    // A step longer than this, after the gate was open a while, moves the follower as far as this
    private static readonly TimeSpan MaxFloorStep = TimeSpan.FromMilliseconds(100);

    private readonly bool _hasVoiceDetector;
    private double? _floorDb;
    private TimeSpan _lastFloorReading;
    private TimeSpan? _lastLoud;
    private TimeSpan? _lastVoice;
    private TimeSpan _lastSpeech;
    private TimeSpan _openedAt;

    // What the readings of the current period showed, see TakeSummary
    private int _intervals;
    private int _openIntervals;
    private int _openings;
    private double _loudestDb = double.NegativeInfinity;
    private double _openPeakSumDb;
    private int _voiceReadings;
    private int _voiceIntervals;

    /// <param name="hasVoiceDetector">Whether voice readings will come, see <see cref="OnVoice"/>.</param>
    public VoiceGateDecision(VoiceGateOptions options, bool hasVoiceDetector = false)
    {
        Options = options;
        _hasVoiceDetector = hasVoiceDetector;
    }

    public VoiceGateOptions Options { get; private set; }

    /// <summary>
    /// Peak level in dBFS from which the gate opens, as set.
    /// </summary>
    public double ThresholdDb => Options.ThresholdDb;

    /// <summary>
    /// Whether the gate waits for the detector to hear a voice: voice detection is on and there is a
    /// detector to do it.
    /// </summary>
    public bool IsVoiceDetectionActive => Options.IsVoiceDetectionEnabled && _hasVoiceDetector;

    /// <summary>
    /// The noise of the room the adaptive threshold follows, or null while it is off or has not
    /// measured yet.
    /// </summary>
    public double? NoiseFloorDb => Options.IsAdaptiveThresholdEnabled ? _floorDb : null;

    /// <summary>
    /// Peak level in dBFS from which the gate opens now: the threshold as set, raised above the
    /// noise of the room by the adaptive threshold.
    /// </summary>
    public double EffectiveThresholdDb => NoiseFloorDb is { } floor
        ? Math.Max(ThresholdDb, Math.Min(floor + NoiseMarginDb, Math.Min(ThresholdDb + MaxAdaptiveRiseDb, 0)))
        : ThresholdDb;

    public bool IsOpen { get; private set; }

    /// <summary>
    /// How long the gate holds after its last loud reading, having spoken for <paramref name="spoken"/>
    /// since it opened.
    /// </summary>
    public static TimeSpan HoldTimeFor(TimeSpan spoken) =>
        TimeSpan.FromTicks(Math.Clamp(MinHoldTime.Ticks + (long)(spoken.Ticks * HoldGrowth),
            MinHoldTime.Ticks, MaxHoldTime.Ticks));

    /// <summary>
    /// Changes how the gate decides. An open gate first waits out its hold, so raising the threshold
    /// above the voice does not cut a word in half.
    /// </summary>
    /// <param name="isOpen">
    /// Whether the valve is open now. It may be open while the decision is not, when the gate was
    /// turned off and is turned on again; it then closes after the hold like any open gate.
    /// </param>
    public void SetOptions(VoiceGateOptions options, TimeSpan now, bool isOpen)
    {
        // The room may have changed since the adaptive threshold was last on
        if (options.IsAdaptiveThresholdEnabled && !Options.IsAdaptiveThresholdEnabled)
            _floorDb = null;

        Options = options;
        _lastSpeech = now;

        if (isOpen && !IsOpen)
            _openedAt = now;

        IsOpen = isOpen;
    }

    /// <summary>
    /// Takes a reading of the loudness.
    /// </summary>
    /// <param name="peakDb">The peak since the previous reading in dBFS.</param>
    /// <returns>Whether the gate opened or closed.</returns>
    public bool OnLevel(double peakDb, TimeSpan now)
    {
        var peak = Math.Max(peakDb, AudioFloorDb);

        Count(peak);

        if (peak >= (IsOpen ? EffectiveThresholdDb - HysteresisDb : EffectiveThresholdDb))
            _lastLoud = now;

        var isChanged = Evaluate(now);

        // After deciding, so the reading that opens the gate already counts as one of an open gate
        if (Options.IsAdaptiveThresholdEnabled)
            FollowFloor(peak, now);

        return isChanged;
    }

    /// <summary>
    /// Takes a reading of the voice detector: the peak of what it let through since the previous
    /// one. It silences what it takes for no voice, so anything above the floor is a voice.
    /// </summary>
    /// <returns>Whether the gate opened or closed.</returns>
    public bool OnVoice(double peakDb, TimeSpan now)
    {
        if (!IsVoiceDetectionActive)
            return false;

        _voiceReadings++;

        if (peakDb > AudioFloorDb)
        {
            _lastVoice = now;
            _voiceIntervals++;
        }

        return Evaluate(now);
    }

    /// <summary>
    /// What the gate did since the previous call, or null when it had no readings.
    /// </summary>
    public VoiceGateSummary? TakeSummary()
    {
        if (_intervals == 0)
            return null;

        var summary = new VoiceGateSummary(ThresholdDb, EffectiveThresholdDb, NoiseFloorDb,
            100.0 * _openIntervals / _intervals, _openings, _loudestDb,
            _openIntervals > 0 ? _openPeakSumDb / _openIntervals : double.NegativeInfinity,
            IsVoiceDetectionActive && _voiceReadings > 0 ? 100.0 * _voiceIntervals / _voiceReadings : null);

        _intervals = 0;
        _openIntervals = 0;
        _openings = 0;
        _loudestDb = double.NegativeInfinity;
        _openPeakSumDb = 0;
        _voiceReadings = 0;
        _voiceIntervals = 0;

        return summary;
    }

    private bool Evaluate(TimeSpan now)
    {
        if (IsRecent(_lastLoud, now) && (!IsVoiceDetectionActive || IsRecent(_lastVoice, now)))
        {
            _lastSpeech = now;

            if (IsOpen)
                return false;

            IsOpen = true;
            _openedAt = now;
            _openings++;
            return true;
        }

        if (!IsOpen || now - _lastSpeech < HoldTimeFor(_lastSpeech - _openedAt))
            return false;

        IsOpen = false;
        return true;
    }

    // Without a detector only the reading at hand counts, as it always did
    private bool IsRecent(TimeSpan? reading, TimeSpan now) =>
        IsVoiceDetectionActive ? now - reading <= VoiceWindow : reading == now;

    private void FollowFloor(double peak, TimeSpan now)
    {
        if (_floorDb is not { } floor)
        {
            // Starts where it raises nothing: a first reading that happens to be loud must not shut the gate
            _floorDb = Math.Min(peak, ThresholdDb - NoiseMarginDb);
            _lastFloorReading = now;
            return;
        }

        var seconds = (now - _lastFloorReading < MaxFloorStep ? now - _lastFloorReading : MaxFloorStep).TotalSeconds;
        _lastFloorReading = now;

        // Only the closed gate hears the room alone
        var rise = IsOpen ? OpenFloorRiseDbPerSecond : FloorRiseDbPerSecond;

        _floorDb = peak > floor
            ? Math.Min(peak, floor + rise * seconds)
            : Math.Max(peak, floor - FloorFallDbPerSecond * seconds);
    }

    // Silence is counted at the floor, as minus infinity would make the average of the open
    // intervals one too
    private void Count(double peak)
    {
        _intervals++;
        _loudestDb = Math.Max(_loudestDb, peak);

        if (!IsOpen)
            return;

        _openIntervals++;
        _openPeakSumDb += peak;
    }
}
