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
/// level messages and moves the valve. With voice detection a second level element measures what
/// RNNoise lets through, which is silence for anything it does not take for a voice.</para>
/// <para>The gate compares peak levels, like the microphone test meter the threshold is set against.</para>
/// <para>The gate learns of a loud interval only once level has measured all of it, so without a
/// delay the start of a word would have passed the closed valve by the time it opens. The branch
/// holds the audio back by <see cref="PreRoll"/> between the level element and the valve instead.</para>
/// <para>The options change from the settings while the level messages arrive on the bus watcher,
/// so both run under one lock: otherwise turning the gate off could open the valve just before a
/// level message closes it again.</para>
/// <para>The valve ramps open and closed, see <see cref="VoiceGateFade"/>: switched at once, the voice
/// would start with a click and its tail would be cut off.</para>
/// </remarks>
public sealed class VoiceGate
{
    /// <summary>
    /// How often the level element reports. Shorter clips less of the first word.
    /// </summary>
    public static readonly TimeSpan LevelInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// How long the audio is held back in front of the valve, so the gate opens before the sound
    /// that opened it reaches the valve. A loud interval is known once level has measured all of it,
    /// <see cref="LevelInterval"/>, and the message has reached the bus watcher, a few milliseconds
    /// more; measured on the branch, the gate opened 5 to 30 ms after the sound started. With 20 ms
    /// the valve is open in time for most onsets and ramps up over the quiet just before them, and
    /// the latest ones lose a few milliseconds where they used to lose all of it. The voice is 20 ms
    /// later for it.
    /// </summary>
    public static readonly TimeSpan PreRoll = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// How much later than the loudness the voice detection hears a sound, added to the
    /// <see cref="PreRoll"/> of a branch that has it. RNNoise gathers 10 ms frames, so a sound may
    /// wait for its frame to fill, and its detector needs a frame or more of a voice to rate it as
    /// one. Measured with synthetic speech, it heard most words 0 to 10 ms after the level did and
    /// a soft onset such as an "h" 60 to 70 ms after; 30 ms keeps the first group whole and the
    /// fade-in of the valve covers part of the second, while the voice is not held back further
    /// for every word. Its level reports over the same interval as the one of the gate, which the
    /// pre-roll already covers.
    /// </summary>
    public static readonly TimeSpan VoiceDetectionDelay = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// The voice probability of RNNoise below which it silences a frame. Measured with synthetic
    /// speech and typing: at 0.5 a twentieth of the 10 ms intervals of the typing passed, enough
    /// for some keys to open the gate; at 0.8 hardly any did, while the share of the speech
    /// that passed fell only from 75 to 70 %, the gaps lying between syllables, which the hold
    /// covers. Higher values cut into the speech without keeping out more.
    /// </summary>
    public const float VoiceActivityThreshold = 0.8f;

    private const string LevelMessageName = "level";
    private const string PeakField = "peak";

    private readonly Element _level;
    private readonly VoiceDetector? _detector;
    private readonly VoiceGateFade _valve;
    private readonly Lock _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly VoiceGateDecision _decision;
    private VoiceGateOptions? _options;

    /// <param name="level">Posts the level messages; silenced while the gate is off.</param>
    /// <param name="valve">The gate valve, closed when the branch is built.</param>
    /// <param name="detector">The voice detection of the branch, null when the runtime has no RNNoise.</param>
    /// <param name="options">How the gate decides, null keeps it open.</param>
    public VoiceGate(string outboundId, Element level, Element valve, VoiceDetector? detector,
        VoiceGateOptions? options)
    {
        OutboundId = outboundId;
        _level = level;
        _detector = detector;
        _valve = new VoiceGateFade(valve);

        // Anything will do while the gate is off, the options that turn it on replace these
        _decision = new VoiceGateDecision(options ?? new VoiceGateOptions(AudioSettings.MaxVoiceGateThresholdDb),
            detector != null);

        lock (_lock)
            Apply(options);
    }

    public string OutboundId { get; }

    public bool IsOpen { get; private set; }

    /// <summary>
    /// Whether the branch has the voice detection; without RNNoise in the runtime the gate goes by the level alone.
    /// </summary>
    public bool HasVoiceDetection => _detector != null;

    /// <summary>
    /// Changes how a running gate decides. Null turns the gate off and opens it. New options are
    /// applied by the next level message, an open gate first waits out its hold time, so raising
    /// the threshold does not cut a word in half.
    /// </summary>
    /// <returns>
    /// Whether the options differ from the ones the gate has. Every save of the audio settings
    /// passes them, and the same ones again would restart the hold of an open gate for nothing.
    /// </returns>
    public bool SetOptions(VoiceGateOptions? options)
    {
        lock (_lock)
        {
            if (options == _options)
                return false;

            Apply(options);
            return true;
        }
    }

    private void Apply(VoiceGateOptions? options)
    {
        _options = options;

        // Levels are measured only while they are needed. RNNoise runs whenever the gate is on, even
        // without voice detection: it takes well under one percent of a core, and it tells a voice
        // from a key far better once it has heard the room for a while than right after it starts,
        // so voice detection turned on in a call works at once.
        _level.SetProperty("post-messages", options != null);
        _detector?.Valve.SetProperty("drop", options == null);
        _detector?.Level.SetProperty("post-messages", options is { IsVoiceDetectionEnabled: true });

        if (options == null)
        {
            SetOpen(true);
            return;
        }

        _decision.SetOptions(options, _clock.Elapsed, IsOpen);
    }

    /// <summary>
    /// Opens or closes the gate by a level message of the gate or of the voice detection. Called
    /// from the bus watcher.
    /// </summary>
    /// <param name="source">The element that posted the message.</param>
    /// <returns>Whether the gate changed its state.</returns>
    public bool Update(IntPtr source, Message message)
    {
        if (TryGetPeak(message) is not { } peak)
            return false;

        lock (_lock)
        {
            // A message posted just before the gate was turned off
            if (_options == null)
                return false;

            var now = _clock.Elapsed;
            var isChanged = source == _detector?.Level.Handle
                ? _decision.OnVoice(peak, now)
                : _decision.OnLevel(peak, now);

            if (!isChanged)
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

    /// <param name="Level">Measures what RNNoise lets through.</param>
    /// <param name="Valve">Feeds RNNoise while the gate is on.</param>
    public sealed record VoiceDetector(Element Level, Element Valve);

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
