namespace Client.Core.Settings;

/// <summary>
/// User preferences of the Logging and debug tab.
/// </summary>
public sealed record DebugSettings
{
    /// <summary>The level of the journal, or null for the one of the build: Debug in Debug builds, Information in Release.</summary>
    public JournalLevel? JournalLevel { get; init; }

    /// <summary>The journal is written to files, not only kept in memory and printed to the console.</summary>
    public bool WriteToFiles { get; init; } = true;

    public NativeLogLevel GStreamerLogLevel { get; init; } = NativeLogLevel.Off;

    public NativeLogLevel LibWebRTCLogLevel { get; init; } = NativeLogLevel.Off;

    /// <summary>Over which screens their statistics are drawn: frames, transport, encoder or decoder.</summary>
    public MediaPerfOverlayMode MediaPerfOverlay { get; init; } = MediaPerfOverlayMode.Off;

    /// <summary>The statistics over a screen start expanded rather than as one line.</summary>
    public bool IsMediaPerfExpanded { get; init; }

    /// <summary>The parts shown when the statistics are expanded.</summary>
    public MediaPerfSections MediaPerfSections { get; init; } = MediaPerfSections.All;

    /// <summary>How often the statistics of the media are read, in milliseconds: one of <see cref="MediaPerfIntervals"/>.</summary>
    public int MediaPerfIntervalMs { get; init; } = 1000;

    /// <summary>The expanded statistics draw a graph of the last half minute of the main values.</summary>
    public bool ShowMediaPerfGraphs { get; init; } = true;

    public MediaPerfJournalMode MediaPerfJournal { get; init; } = MediaPerfJournalMode.Off;

    /// <summary>How often the summary goes into the journal, in seconds: one of <see cref="MediaPerfSummaryPeriods"/>.</summary>
    public int MediaPerfSummarySeconds { get; init; } = 10;

    /// <summary>
    /// A warning goes into the journal when a stream runs into trouble: an encoder or decoder slower
    /// than the frame rate, a decoder that falls behind, a freeze, a bitrate held back by the network.
    /// </summary>
    public bool LogMediaPerfAnomalies { get; init; } = true;

    public static readonly int[] MediaPerfIntervals = [500, 1000, 2000];

    public static readonly int[] MediaPerfSummaryPeriods = [5, 10, 30, 60];

    public TimeSpan MediaPerfInterval => TimeSpan.FromMilliseconds(MediaPerfIntervalMs);

    /// <summary>Anything reads the statistics of the media, so the transports measure them.</summary>
    public bool IsMediaPerfNeeded => MediaPerfOverlay != MediaPerfOverlayMode.Off ||
                                     MediaPerfJournal != MediaPerfJournalMode.Off || LogMediaPerfAnomalies;

    /// <summary>
    /// The same settings with every value that cannot be chosen replaced by its default.
    /// </summary>
    public DebugSettings Normalize() => this with
    {
        JournalLevel = JournalLevel is { } level && Enum.IsDefined(level) ? level : null,
        GStreamerLogLevel = Enum.IsDefined(GStreamerLogLevel) ? GStreamerLogLevel : NativeLogLevel.Off,
        LibWebRTCLogLevel = Enum.IsDefined(LibWebRTCLogLevel) ? LibWebRTCLogLevel : NativeLogLevel.Off,
        MediaPerfOverlay = Enum.IsDefined(MediaPerfOverlay) ? MediaPerfOverlay : MediaPerfOverlayMode.Off,
        MediaPerfSections = MediaPerfSections & MediaPerfSections.All,
        MediaPerfIntervalMs = MediaPerfIntervals.Contains(MediaPerfIntervalMs) ? MediaPerfIntervalMs : 1000,
        MediaPerfJournal = Enum.IsDefined(MediaPerfJournal) ? MediaPerfJournal : MediaPerfJournalMode.Off,
        MediaPerfSummarySeconds = MediaPerfSummaryPeriods.Contains(MediaPerfSummarySeconds) ? MediaPerfSummarySeconds : 10
    };

    public NativeLogLevel LogLevelOf(NativeLogSource source) => source switch
    {
        NativeLogSource.GStreamer => GStreamerLogLevel,
        NativeLogSource.LibWebRTC => LibWebRTCLogLevel,
        _ => NativeLogLevel.Off
    };

    /// <summary>
    /// The settings in effect, read by the views that show what is debugged, e.g. the statistics over a screen.
    /// </summary>
    public static DebugSettings Current { get; private set; } = new();

    /// <summary>Raised after <see cref="Current"/> changed, on the thread that changed it.</summary>
    public static event Action? Changed;

    public static void Apply(DebugSettings settings)
    {
        Current = settings.Normalize();
        Changed?.Invoke();
    }
}
