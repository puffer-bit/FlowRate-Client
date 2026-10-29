using Client.Core.Media;
using Shared.Models.Media;

namespace Client.Core.Sessions.Hybrid;

/// <summary>
/// Tells what is wrong with a watched screen from the readings of the transport statistics and the
/// frames its picture got meanwhile. An issue is raised with the first reading that shows it, as a
/// reading already covers a couple of seconds, and kept until two readings in a row show it no
/// more, so a badge does not blink with every burst.
/// </summary>
public sealed class StreamHealthAnalyzer
{
    // A stream that has just started may have nothing to show yet
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(4);
    private const int ClearReadings = 2;

    private const double LossWarningPercent = 2;
    private const double LossCriticalPercent = 8;
    private const double JitterWarningMs = 30;
    private const double JitterCriticalMs = 80;
    private const double DropWarningShare = 0.15;
    private const double DropCriticalShare = 0.4;

    // Fewer frames tell nothing about the drawing
    private const long MinFramesForDrops = 10;

    // Drops while a frame came this much later than the fastest one, and later than this many frames,
    // are the network's: the picture waits only a couple of frames, and the renderer keeps up with far
    // more than a stream sends
    private const double BurstyLateMs = 60;
    private const double BurstyLateFrames = 4;

    private readonly int _userId;
    private readonly TimeSpan _startedAt;
    private readonly Dictionary<StreamIssueKind, Tracked> _tracked = [];

    private TimeSpan _previousAt;
    private FrameStats? _previousFrames;

    public IReadOnlyList<StreamIssue> Issues { get; private set; } = [];

    /// <param name="userId">The user who shares the screen.</param>
    /// <param name="startedAt">When watching started, on the clock the readings are timed with.</param>
    public StreamHealthAnalyzer(int userId, TimeSpan startedAt)
    {
        _userId = userId;
        _startedAt = startedAt;
        _previousAt = startedAt;
    }

    /// <param name="frames">The counters of the picture now, or null while it is not drawn.</param>
    /// <param name="isAudioExpected">The screen is shared with its sound.</param>
    /// <param name="now">The time of the reading.</param>
    /// <returns>The issues changed.</returns>
    public bool Update(TransportStatsSample sample, FrameStats? frames, bool isAudioExpected, TimeSpan now)
    {
        var found = Detect(sample, frames, isAudioExpected, now);

        foreach (var issue in found)
        {
            // An issue first seen now has lasted since the previous reading
            if (!_tracked.TryGetValue(issue.Kind, out var tracked))
                _tracked[issue.Kind] = tracked = new Tracked { Since = _previousAt, Issue = issue };

            tracked.CleanReadings = 0;
            tracked.Issue = IsLasting(issue.Kind) ? issue with { Value = (now - tracked.Since).TotalSeconds } : issue;
        }

        foreach (var (kind, tracked) in _tracked.ToList())
        {
            if (found.All(issue => issue.Kind != kind) && ++tracked.CleanReadings >= ClearReadings)
                _tracked.Remove(kind);
        }

        _previousAt = now;
        _previousFrames = frames;

        var issues = _tracked.Values.Select(tracked => tracked.Issue).OrderBy(issue => issue.Kind).ToList();
        var isChanged = !issues.SequenceEqual(Issues);
        Issues = issues;

        return isChanged;
    }

    private List<StreamIssue> Detect(TransportStatsSample sample, FrameStats? frames, bool isAudioExpected, TimeSpan now)
    {
        var found = new List<StreamIssue>();
        var isStarted = now - _startedAt >= StartGrace;
        var video = Find(sample, MediaType.Video);
        var videoPackets = video?.Packets ?? 0;

        if (video is { } stream)
        {
            if (stream.LossPercent >= LossWarningPercent)
                found.Add(new StreamIssue(StreamIssueKind.PacketLoss,
                    stream.LossPercent >= LossCriticalPercent ? StreamIssueSeverity.Critical : StreamIssueSeverity.Warning,
                    stream.LossPercent));

            if (stream.JitterMs >= JitterWarningMs)
                found.Add(new StreamIssue(StreamIssueKind.HighJitter,
                    stream.JitterMs >= JitterCriticalMs ? StreamIssueSeverity.Critical : StreamIssueSeverity.Warning,
                    stream.JitterMs));
        }

        // A still screen keeps sending its last frame a few times a second, so silence is the streamer's
        if (isStarted && videoPackets == 0)
            found.Add(new StreamIssue(StreamIssueKind.NoSourceFrames, StreamIssueSeverity.Critical));

        if (frames is { } current && _previousFrames is { } last)
        {
            var received = current.Received - last.Received;
            var dropped = current.Dropped - last.Dropped;
            var seconds = (now - _previousAt).TotalSeconds;

            if (isStarted && videoPackets > 0 && received == 0)
            {
                // A keyframe asked for in an earlier reading may still be on its way
                var isWaiting = video!.Value.KeyframeRequests > 0 || _tracked.ContainsKey(StreamIssueKind.WaitingForKeyframe);
                found.Add(new StreamIssue(isWaiting ? StreamIssueKind.WaitingForKeyframe : StreamIssueKind.DecoderStalled,
                    StreamIssueSeverity.Critical));
            }
            else if (received >= MinFramesForDrops && seconds > 0)
            {
                var share = (double)dropped / received;

                if (share >= DropWarningShare)
                {
                    var severity = share >= DropCriticalShare ? StreamIssueSeverity.Critical : StreamIssueSeverity.Warning;
                    var frameMs = seconds * 1000 / received;

                    found.Add(current.LateMs >= Math.Max(BurstyLateMs, BurstyLateFrames * frameMs)
                        ? new StreamIssue(StreamIssueKind.BurstyArrival, severity, current.LateMs)
                        : new StreamIssue(StreamIssueKind.RendererBehind, severity, dropped / seconds,
                            received / seconds));
                }
            }
        }

        if (isStarted && isAudioExpected && (Find(sample, MediaType.Audio)?.Packets ?? 0) == 0)
            found.Add(new StreamIssue(StreamIssueKind.NoAudio, StreamIssueSeverity.Warning));

        return found;
    }

    private InboundStreamSample? Find(TransportStatsSample sample, MediaType mediaType)
    {
        foreach (var stream in sample.Inbound ?? [])
        {
            if (stream.UserId == _userId && stream.SourceType == MediaSourceType.Screen && stream.MediaType == mediaType)
                return stream;
        }

        return null;
    }

    // Issues whose figure is how long they last
    private static bool IsLasting(StreamIssueKind kind) => kind is StreamIssueKind.WaitingForKeyframe
        or StreamIssueKind.NoSourceFrames or StreamIssueKind.NoAudio or StreamIssueKind.DecoderStalled;

    private sealed class Tracked
    {
        public TimeSpan Since { get; init; }
        public required StreamIssue Issue { get; set; }
        public int CleanReadings { get; set; }
    }
}
