namespace Client.Core.Settings;

/// <summary>Over which screens the statistics of the media are drawn.</summary>
public enum MediaPerfOverlayMode
{
    Off = 0,
    AllStreams = 1,

    /// <summary>Only over the screen expanded to the whole view.</summary>
    ExpandedOnly = 2
}

/// <summary>What of the statistics of the media goes into the journal.</summary>
public enum MediaPerfJournalMode
{
    Off = 0,

    /// <summary>A line per stream every <see cref="DebugSettings.MediaPerfSummarySeconds"/>.</summary>
    Summary = 1,

    /// <summary>The summary, and every window in a JSON Lines file of its own next to the journal.</summary>
    Detailed = 2
}

/// <summary>The parts of the statistics drawn over a screen when they are expanded.</summary>
[Flags]
public enum MediaPerfSections
{
    None = 0,

    /// <summary>The connection to the SFU: round trip, loss, bandwidth estimate, TCP.</summary>
    Network = 1,

    /// <summary>The transport of the picture: jitter buffer, freezes, keyframe requests, pacer.</summary>
    Transport = 2,

    /// <summary>The encoder of our screen or the decoder of a watched one.</summary>
    Codec = 4,

    /// <summary>How the frames are drawn: shown, dropped, late, the way to the screen.</summary>
    Render = 8,

    /// <summary>The jitter buffer of the sound of the screen.</summary>
    Audio = 16,

    All = Network | Transport | Codec | Render | Audio
}
