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

    /// <summary>The count of shown and dropped frames is drawn over every screen.</summary>
    public bool ShowFrameStats { get; init; }

    /// <summary>
    /// The same settings with every value that cannot be chosen replaced by its default.
    /// </summary>
    public DebugSettings Normalize() => this with
    {
        JournalLevel = JournalLevel is { } level && Enum.IsDefined(level) ? level : null,
        GStreamerLogLevel = Enum.IsDefined(GStreamerLogLevel) ? GStreamerLogLevel : NativeLogLevel.Off,
        LibWebRTCLogLevel = Enum.IsDefined(LibWebRTCLogLevel) ? LibWebRTCLogLevel : NativeLogLevel.Off
    };

    public NativeLogLevel LogLevelOf(NativeLogSource source) => source switch
    {
        NativeLogSource.GStreamer => GStreamerLogLevel,
        NativeLogSource.LibWebRTC => LibWebRTCLogLevel,
        _ => NativeLogLevel.Off
    };

    /// <summary>
    /// The settings in effect, read by the views that show what is debugged, e.g. the frame counter.
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
