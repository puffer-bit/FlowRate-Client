using Client.Core.Settings;

namespace Client.Core.Media;

/// <summary>
/// What a voice gate did over a period, for the log.
/// </summary>
/// <param name="ThresholdDb">The level the gate opens from.</param>
/// <param name="OpenPercent">The share of the period the gate was open.</param>
/// <param name="Openings">How many times it opened.</param>
/// <param name="LoudestDb">The loudest peak of the period.</param>
/// <param name="AverageOpenPeakDb">The average peak while the gate was open.</param>
public sealed record VoiceGateSummary(double ThresholdDb, double OpenPercent, int Openings, double LoudestDb,
    double AverageOpenPeakDb);

/// <summary>
/// Decides from the measured loudness whether the voice gate of a microphone is open. It knows
/// nothing of how the loudness is measured: every reading comes with the time it was taken, so the
/// same logic runs in the transport, in the microphone test and in the tests.
/// </summary>
/// <remarks>
/// <para>The gate opens at the threshold and stays open down to <see cref="HysteresisDb"/> below it,
/// so a word that fades out does not switch it on and off around a single value.</para>
/// <para>After the last loud reading it holds for a while, which covers the pauses between words. The
/// hold grows with how long the gate has been speaking: a knock or a cough that opened it is cut
/// short after <see cref="MinHoldTime"/>, while a sentence keeps up to <see cref="MaxHoldTime"/>
/// between its words, so its end is not chopped.</para>
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

    private TimeSpan? _lastLoud;
    private TimeSpan _lastSpeech;
    private TimeSpan _openedAt;

    // What the readings of the current period showed, see TakeSummary
    private int _intervals;
    private int _openIntervals;
    private int _openings;
    private double _loudestDb = double.NegativeInfinity;
    private double _openPeakSumDb;

    public VoiceGateDecision(VoiceGateOptions options)
    {
        Options = options;
    }

    public VoiceGateOptions Options { get; private set; }

    /// <summary>
    /// Peak level in dBFS from which the gate opens, as set.
    /// </summary>
    public double ThresholdDb => Options.ThresholdDb;

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

        if (peak >= (IsOpen ? ThresholdDb - HysteresisDb : ThresholdDb))
            _lastLoud = now;

        return Evaluate(now);
    }

    /// <summary>
    /// What the gate did since the previous call, or null when it had no readings.
    /// </summary>
    public VoiceGateSummary? TakeSummary()
    {
        if (_intervals == 0)
            return null;

        var summary = new VoiceGateSummary(ThresholdDb, 100.0 * _openIntervals / _intervals, _openings, _loudestDb,
            _openIntervals > 0 ? _openPeakSumDb / _openIntervals : double.NegativeInfinity);

        _intervals = 0;
        _openIntervals = 0;
        _openings = 0;
        _loudestDb = double.NegativeInfinity;
        _openPeakSumDb = 0;

        return summary;
    }

    private bool Evaluate(TimeSpan now)
    {
        // Only the reading at hand counts
        if (_lastLoud == now)
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
