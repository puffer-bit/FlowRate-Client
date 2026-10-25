using System.Diagnostics;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// Sends the microphone only while the user speaks. The level element of the branch reports the
/// loudness, and the gate opens its valve above a threshold, so the noise of the room never leaves
/// the machine and the others can tell who speaks just by whether packets arrive.
/// </summary>
/// <remarks>
/// <para>The gate compares peak levels, like the microphone test meter the threshold is set against.</para>
/// <para>The gate learns of a loud interval only after it has passed the valve, so it opens one
/// <see cref="LevelInterval"/> late and the very start of a word is lost.</para>
/// <para>The threshold changes from the settings while the level messages arrive on the bus watcher,
/// so both run under one lock: otherwise turning the gate off could open the valve just before a
/// level message closes it again.</para>
/// <para>The valve ramps open and closed, see <see cref="VoiceGateFade"/>: switched at once, the voice
/// would start with a click and its tail would be cut off.</para>
/// </remarks>
internal sealed class VoiceGate
{
    /// <summary>
    /// How often the level element reports. Shorter clips less of the first word.
    /// </summary>
    public static readonly TimeSpan LevelInterval = TimeSpan.FromMilliseconds(10);

    // Closing lower than opening keeps a fading word from switching the gate on and off
    private const double HysteresisDb = 6;

    // Covers the pauses between words, so a sentence goes out in one piece
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(300);

    private const string LevelMessageName = "level";
    private const string PeakField = "peak";

    private readonly Element _level;
    private readonly VoiceGateFade _valve;
    private readonly Lock _lock = new();
    private double? _thresholdDb;
    private long _lastLoudTimestamp;

    /// <param name="level">Posts the level messages; silenced while the gate is off.</param>
    /// <param name="valve">The gate valve, closed when the branch is built.</param>
    /// <param name="thresholdDb">Peak level in dBFS from which the gate opens, null keeps it open.</param>
    public VoiceGate(string outboundId, Element level, Element valve, double? thresholdDb)
    {
        OutboundId = outboundId;
        _level = level;
        _valve = new VoiceGateFade(valve);
        SetThreshold(thresholdDb);
    }

    public string OutboundId { get; }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Changes the threshold of a running gate. Null turns the gate off and opens it. A new
    /// threshold is applied by the next level message, an open gate first waits out its hold time,
    /// so raising the threshold does not cut a word in half.
    /// </summary>
    public void SetThreshold(double? thresholdDb)
    {
        lock (_lock)
        {
            _thresholdDb = thresholdDb;
            _lastLoudTimestamp = Stopwatch.GetTimestamp();

            // Levels are measured only while they are needed
            _level.SetProperty("post-messages", thresholdDb != null);

            if (thresholdDb == null)
                SetOpen(true);
        }
    }

    /// <summary>
    /// Opens or closes the gate by the loudness in a level message. Called from the bus watcher.
    /// </summary>
    /// <returns>Whether the gate changed its state.</returns>
    public bool Update(Message message)
    {
        if (TryGetPeak(message) is not { } peak)
            return false;

        lock (_lock)
        {
            // A message posted just before the gate was turned off
            if (_thresholdDb is not { } openThresholdDb)
                return false;

            var now = Stopwatch.GetTimestamp();

            if (peak >= (IsOpen ? openThresholdDb - HysteresisDb : openThresholdDb))
            {
                _lastLoudTimestamp = now;

                if (IsOpen)
                    return false;

                SetOpen(true);
                return true;
            }

            if (!IsOpen || Stopwatch.GetElapsedTime(_lastLoudTimestamp, now) < HoldTime)
                return false;

            SetOpen(false);
            return true;
        }
    }

    private void SetOpen(bool isOpen)
    {
        IsOpen = isOpen;
        _valve.Set(isOpen);
    }

    /// <summary>
    /// The loudest channel of a level message in dBFS, or null for any other message.
    /// </summary>
    private static double? TryGetPeak(Message message)
    {
        using var structure = message.GetStructure();

        if (structure?.GetName() != LevelMessageName || !structure.HasField(PeakField))
            return null;

        using var value = structure.GetValue(PeakField);

        return GstNative.ReadLargestDouble(value);
    }
}
