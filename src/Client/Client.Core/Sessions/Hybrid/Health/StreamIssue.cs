namespace Client.Core.Sessions.Hybrid;

/// <summary>What is wrong with a watched screen.</summary>
public enum StreamIssueKind
{
    /// <summary>Packets of the picture are lost on the way.</summary>
    PacketLoss,

    /// <summary>The packets come unevenly, so the picture is held back to smooth them out.</summary>
    HighJitter,

    /// <summary>
    /// Frames are dropped because the network holds them back and lets them through in a bunch, more
    /// than the picture waits for.
    /// </summary>
    BurstyArrival,

    /// <summary>A loss broke the picture, which stays frozen until the sender sends a keyframe.</summary>
    WaitingForKeyframe,

    /// <summary>Nothing comes from the streamer while the connection is alive.</summary>
    NoSourceFrames,

    /// <summary>The screen is shared with its sound, but no sound comes.</summary>
    NoAudio,

    /// <summary>Packets come, but the decoder gives no frames.</summary>
    DecoderStalled,

    /// <summary>Decoded frames are dropped before they are drawn.</summary>
    RendererBehind
}

/// <summary>Where the trouble is; the screen shows one badge per area, in this order.</summary>
public enum StreamIssueArea
{
    Network,
    Source,
    Audio,
    Performance
}

public enum StreamIssueSeverity
{
    Warning,
    Critical
}

/// <param name="Value">
/// The figure behind the issue: the percent lost, the jitter in ms, how late the latest frame came in
/// ms, the seconds it lasts, or the frames dropped per second.
/// </param>
/// <param name="Total">The frames received per second, for the frames dropped of them; 0 otherwise.</param>
public sealed record StreamIssue(StreamIssueKind Kind, StreamIssueSeverity Severity, double Value = 0, double Total = 0)
{
    public StreamIssueArea Area => Kind switch
    {
        StreamIssueKind.PacketLoss or StreamIssueKind.HighJitter or StreamIssueKind.BurstyArrival
            or StreamIssueKind.WaitingForKeyframe =>
            StreamIssueArea.Network,
        StreamIssueKind.NoSourceFrames => StreamIssueArea.Source,
        StreamIssueKind.NoAudio => StreamIssueArea.Audio,
        _ => StreamIssueArea.Performance
    };
}
