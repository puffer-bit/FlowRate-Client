using System.Diagnostics;
using Client.Core.Media;
using Client.Core.Settings;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// Sends the microphone only while the user speaks. The level element of the branch reports the
/// loudness, and the gate opens its valve above a threshold, so the noise of the room never leaves
/// the machine and the others can tell who speaks just by whether packets arrive.
/// </summary>
/// <remarks>
/// <para>When to open and close is up to <see cref="VoiceGateDecision"/>; this class feeds it the
/// level messages and moves the valve.</para>
/// <para>The gate compares peak levels, like the microphone test meter the threshold is set against.</para>
/// <para>The gate learns of a loud interval only after it has passed the valve, so it opens one
/// <see cref="LevelInterval"/> late and the very start of a word is lost.</para>
/// <para>The options change from the settings while the level messages arrive on the bus watcher,
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

    private const string LevelMessageName = "level";
    private const string PeakField = "peak";

    private readonly Element _level;
    private readonly VoiceGateFade _valve;
    private readonly Lock _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly VoiceGateDecision _decision;
    private VoiceGateOptions? _options;

    /// <param name="level">Posts the level messages; silenced while the gate is off.</param>
    /// <param name="valve">The gate valve, closed when the branch is built.</param>
    /// <param name="options">How the gate decides, null keeps it open.</param>
    public VoiceGate(string outboundId, Element level, Element valve, VoiceGateOptions? options)
    {
        OutboundId = outboundId;
        _level = level;
        _valve = new VoiceGateFade(valve);

        // Anything will do while the gate is off, the options that turn it on replace these
        _decision = new VoiceGateDecision(options ?? new VoiceGateOptions(AudioSettings.MaxVoiceGateThresholdDb));
        SetOptions(options);
    }

    public string OutboundId { get; }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Changes how a running gate decides. Null turns the gate off and opens it. New options are
    /// applied by the next level message, an open gate first waits out its hold time, so raising
    /// the threshold does not cut a word in half.
    /// </summary>
    public void SetOptions(VoiceGateOptions? options)
    {
        lock (_lock)
        {
            _options = options;

            // Levels are measured only while they are needed
            _level.SetProperty("post-messages", options != null);

            if (options == null)
            {
                SetOpen(true);
                return;
            }

            _decision.SetOptions(options, _clock.Elapsed, IsOpen);
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
            if (_options == null || !_decision.OnLevel(peak, _clock.Elapsed))
                return false;

            SetOpen(_decision.IsOpen);
            return true;
        }
    }

    /// <summary>
    /// What the gate did since the previous call, or null when it measured nothing, as while it is off.
    /// </summary>
    public VoiceGateSummary? TakeSummary()
    {
        lock (_lock)
            return _options == null ? null : _decision.TakeSummary();
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
