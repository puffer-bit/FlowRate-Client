using Client.Core.Sessions.Hybrid;
using Shared.Models.Media;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Reads the statistics of webrtcbin on a timer and logs, for every RTP stream, what happened to it
/// since the last reading: the bitrate, the loss, the jitter, the round trip and the repair requests.
/// Also logs the candidate pair the media goes over whenever ICE picks another one, and reports the
/// round trip and the loss of every reading, which the user sees as the quality of the connection.
/// </summary>
/// <remarks>
/// The figures of every reading are meant for measuring the transport, so they go to the debug log
/// only; a summary of every minute goes to the information log, so a release build still tells how
/// a long session went, and a reading with a burst of loss is a warning. A stream is recognized by its SSRC, which is the one the SFU created the producer or the
/// consumer with. The pair is logged as information: ICE may move the media to a worse pair on a bad
/// network, from a UDP to a TCP candidate of the SFU, and stay there while the connection reports
/// itself connected all along.
/// </remarks>
internal sealed partial class TransportStats
{
    // How often the statistics are read. Short enough for a graph of the connection to follow it,
    // long enough for the counters to move between two readings
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // How often the figures are summed up in the information log, in readings: once a minute
    private const int ReadingsPerSummary = 30;

    // A reading that lost more of a stream is worth a warning even without the debug log; a few
    // packets of a quiet stream do not make a burst
    private const double LossWarningPercent = 5;
    private const long LossWarningPackets = 5;

    private readonly TransportCore _core;
    private readonly Func<uint, InboundTrack?> _findInbound;
    private readonly Func<uint, OutboundTrack?> _findOutbound;
    private readonly Func<uint, (long Lost, long Late)?> _readJitterBuffer;
    private readonly Func<List<(OutboundTrack Track, double LevelMs, uint Packets)>> _readSendQueues;
    private readonly Action _reportPeriod;
    private readonly Action<TransportStatsSample> _sampled;
    private readonly ILogger _logger;

    // For a stream of no known track. The kind webrtcbin reports is not used instead: with every
    // section bundled it often names the kind of another section
    private const string UnknownSource = "unknown";

    // The counters of the previous reading by SSRC, which the figures of the next one are the
    // difference to. Only on the thread that answers the promise, one reading at a time
    private readonly Dictionary<uint, Counters> _previous = [];
    private double _previousTimestamp;
    private string? _selectedPair;
    private bool? _isOverTcp;

    // The counters at the start of the current summary, by SSRC, and when it started
    private readonly Dictionary<uint, Counters> _summaryStart = [];
    private double _summaryStartTimestamp;
    private int _requests;

    // What the last queue of a track we send may hold before it is reported, in ms. Packets wait
    // there only while the socket does not take them, which over UDP it always does
    private const double SendQueueWarningMs = 100;

    private System.Threading.Timer? _timer;
    private volatile bool _isStopped;
    private int _readingInFlight;

    /// <param name="findInbound">The inbound track with the given SSRC.</param>
    /// <param name="findOutbound">The outbound track with the given SSRC.</param>
    /// <param name="readJitterBuffer">What the jitter buffer of the stream with the given SSRC lost and got late.</param>
    /// <param name="readSendQueues">What the last queue of every track we send holds.</param>
    /// <param name="reportPeriod">Logs the figures of the media itself, once per summary.</param>
    /// <param name="sampled">Takes the round trip and the loss of every reading, on a thread of webrtcbin.</param>
    public TransportStats(TransportCore core, Func<uint, InboundTrack?> findInbound,
        Func<uint, OutboundTrack?> findOutbound, Func<uint, (long Lost, long Late)?> readJitterBuffer,
        Func<List<(OutboundTrack Track, double LevelMs, uint Packets)>> readSendQueues, Action reportPeriod,
        Action<TransportStatsSample> sampled, ILogger logger)
    {
        _core = core;
        _findInbound = findInbound;
        _findOutbound = findOutbound;
        _readJitterBuffer = readJitterBuffer;
        _readSendQueues = readSendQueues;
        _reportPeriod = reportPeriod;
        _sampled = sampled;
        _logger = logger;
    }

    public void Start()
    {
        _isStopped = false;
        _timer = new System.Threading.Timer(_ => Request(), null, PollInterval, PollInterval);
    }

    /// <summary>
    /// Stops reading. Waits for a request being made, so webrtcbin is not touched after this returns;
    /// a reading already requested is dropped when it arrives.
    /// </summary>
    public void Stop()
    {
        _isStopped = true;

        using (var stopped = new ManualResetEvent(false))
        {
            if (_timer?.Dispose(stopped) == true)
                stopped.WaitOne();
        }

        _timer = null;
    }

    private void Request()
    {
        try
        {
            var webrtc = _core.WebRTC;

            if (_isStopped || _core.IsDisposed || webrtc == null)
                return;

            ReportSendQueues();

            if (++_requests % ReadingsPerSummary == 0)
                _reportPeriod();

            // A transport that is busy answers late; a second request would only pile up behind it
            if (Interlocked.Exchange(ref _readingInFlight, 1) == 1)
                return;

            // Not disposed here: webrtcbin answers the promise later, see Negotiator.CreatePromise
            var promise = Promise.NewWithChangeFunc(OnReply);

            // Without a pad the statistics of every stream come back
            webrtc.EmitSignal("get-stats", null!, promise);
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _readingInFlight, 0);
            LogStatsRequestFailed(exception, _core.SessionId);
        }
    }

    /// <summary>
    /// Logs how much of every track we send waits in front of webrtcbin. The socket takes the packets
    /// on the streaming thread of the track, so one that stops taking them, as a TCP one does while
    /// it repairs a loss, fills the last queue of the branch; the voice queue then drops what is
    /// older than 200 ms and the listeners hear us break up. Over UDP the queue stays empty.
    /// </summary>
    private void ReportSendQueues()
    {
        foreach (var (track, levelMs, packets) in _readSendQueues())
        {
            if (levelMs >= SendQueueWarningMs)
                LogSendQueueBacklog(_core.SessionId, track.SourceType.ToString(), track.SSRC, levelMs, packets);
            else
                LogSendQueue(_core.SessionId, track.SourceType.ToString(), track.SSRC, levelMs);
        }
    }

    /// <summary>
    /// Runs on a thread of webrtcbin through a native callback, where an exception would tear the
    /// process down.
    /// </summary>
    private void OnReply(Promise promise)
    {
        try
        {
            if (_isStopped || _core.IsDisposed)
                return;

            using var reply = promise.GetReply();

            if (reply != null)
                Report(reply);
        }
        catch (Exception exception)
        {
            LogStatsReadFailed(exception, _core.SessionId);
        }
        finally
        {
            Interlocked.Exchange(ref _readingInFlight, 0);
        }
    }

    private void Report(Structure reply)
    {
        var inbound = new List<Counters>();
        var outbound = new List<Counters>();
        var remote = new Dictionary<uint, RemoteReport>();
        var candidates = new Dictionary<string, string>();
        var pairs = new Dictionary<string, (string? Local, string? Remote)>();
        string? selectedPairId = null;
        double timestamp = 0;

        // Every field is a structure of its own, named after the type of the statistics it holds
        for (uint index = 0; index < reply.NFields(); index++)
        {
            using var value = reply.GetValue(reply.NthFieldName(index));
            using var stats = value.GetBoxed<Structure>();

            if (stats == null)
                continue;

            switch (stats.GetName())
            {
                case "inbound-rtp":
                    inbound.Add(ReadInbound(stats));
                    break;

                case "outbound-rtp":
                    outbound.Add(ReadOutbound(stats));
                    break;

                case "remote-inbound-rtp":
                    var report = ReadRemoteInbound(stats);
                    remote[report.Ssrc] = report;
                    break;

                case "transport":
                    selectedPairId ??= stats.GetString("selected-candidate-pair-id");
                    continue;

                case "candidate-pair":
                    if (stats.GetString("id") is { } pairId)
                        pairs[pairId] = (stats.GetString("local-candidate-id"), stats.GetString("remote-candidate-id"));
                    continue;

                case "local-candidate" or "remote-candidate":
                    if (stats.GetString("id") is { } candidateId)
                        candidates[candidateId] = DescribeCandidate(stats);
                    continue;

                default:
                    continue;
            }

            if (stats.GetDouble("timestamp", out var time))
                timestamp = Math.Max(timestamp, time);
        }

        ReportSelectedPair(selectedPairId, pairs, candidates);

        // The timestamps are in milliseconds. The first reading has nothing to be compared to
        var seconds = (timestamp - _previousTimestamp) / 1000;
        var isFirst = _previous.Count == 0;
        var isDebug = _logger.IsEnabled(LogLevel.Debug);

        _previousTimestamp = timestamp;

        // A stream that never carried a packet is the SSRC the other side sends its reports from
        foreach (var current in inbound.Where(stream => stream.Packets > 0))
        {
            if (isFirst || seconds <= 0 || !_previous.TryGetValue(current.Ssrc, out var last))
                continue;

            var delta = current - last;

            if (isDebug)
                LogInbound(LogLevel.Debug, current, delta, seconds);
            else if (delta.Lost >= LossWarningPackets && LossPercent(delta) >= LossWarningPercent)
                LogInbound(LogLevel.Warning, current, delta, seconds);
        }

        if (isDebug)
        {
            foreach (var current in outbound.Where(stream => stream.Packets > 0))
            {
                if (!isFirst && seconds > 0 && _previous.TryGetValue(current.Ssrc, out var last))
                    LogOutbound(LogLevel.Debug, current, current - last, seconds, remote.GetValueOrDefault(current.Ssrc));
            }
        }

        if (!isFirst && seconds > 0)
            _sampled(Sample(inbound, outbound, remote));

        // A stream that is gone is forgotten with the reading it was last in
        _previous.Clear();

        foreach (var current in inbound.Concat(outbound))
            _previous[current.Ssrc] = current;

        Summarize(inbound, outbound, remote, timestamp);
    }

    /// <summary>
    /// Logs what every stream did since the previous summary, once per <see cref="ReadingsPerSummary"/>
    /// readings' worth of time. A stream that came up meanwhile starts with the next one.
    /// </summary>
    private void Summarize(List<Counters> inbound, List<Counters> outbound, Dictionary<uint, RemoteReport> remote,
        double timestamp)
    {
        var seconds = (timestamp - _summaryStartTimestamp) / 1000;

        if (_summaryStart.Count > 0 && seconds < ReadingsPerSummary * PollInterval.TotalSeconds - 1)
            return;

        if (_summaryStart.Count > 0 && seconds > 0)
        {
            foreach (var current in inbound.Where(stream => stream.Packets > 0))
            {
                if (_summaryStart.TryGetValue(current.Ssrc, out var start))
                    LogInbound(LogLevel.Information, current, current - start, seconds);
            }

            foreach (var current in outbound.Where(stream => stream.Packets > 0))
            {
                if (_summaryStart.TryGetValue(current.Ssrc, out var start))
                    LogOutbound(LogLevel.Information, current, current - start, seconds,
                        remote.GetValueOrDefault(current.Ssrc));
            }
        }

        _summaryStart.Clear();
        _summaryStartTimestamp = timestamp;

        foreach (var current in inbound.Concat(outbound))
            _summaryStart[current.Ssrc] = current;
    }

    private void ReportSelectedPair(string? pairId, Dictionary<string, (string? Local, string? Remote)> pairs,
        Dictionary<string, string> candidates)
    {
        if (pairId == null || !pairs.TryGetValue(pairId, out var pair))
            return;

        var local = pair.Local == null ? null : candidates.GetValueOrDefault(pair.Local);
        var remote = pair.Remote == null ? null : candidates.GetValueOrDefault(pair.Remote);

        if (local == null || remote == null)
            return;

        // The description starts with the protocol of the candidate
        _isOverTcp = local.StartsWith("tcp", StringComparison.OrdinalIgnoreCase)
                     || remote.StartsWith("tcp", StringComparison.OrdinalIgnoreCase);

        var description = $"{local} -> {remote}";

        if (description == _selectedPair)
            return;

        var isFirst = _selectedPair == null;
        _selectedPair = description;

        if (isFirst)
            LogCandidatePair(_core.SessionId, local, remote);
        else
            LogCandidatePairChanged(_core.SessionId, local, remote);
    }

    /// <summary>
    /// The protocol, the type and the address of a candidate, e.g. "udp host 10.0.0.2:50000".
    /// </summary>
    private static string DescribeCandidate(Structure stats)
    {
        stats.GetUint("port", out var port);

        return $"{stats.GetString("protocol")} {stats.GetString("candidate-type")} {stats.GetString("address")}:{port}";
    }

    /// <summary>
    /// The loss of all the streams together since the previous reading, the round trip of the
    /// streams we send, and what every stream we receive did. Called before the counters of this
    /// reading replace the previous ones.
    /// </summary>
    private TransportStatsSample Sample(List<Counters> inbound, List<Counters> outbound,
        Dictionary<uint, RemoteReport> remote)
    {
        long received = 0;
        long lost = 0;
        var streams = new List<InboundStreamSample>();

        foreach (var current in inbound.Where(stream => stream.Packets > 0))
        {
            if (!_previous.TryGetValue(current.Ssrc, out var last))
                continue;

            var delta = current - last;
            received += Math.Max(0, delta.Packets);
            lost += Math.Max(0, delta.Lost);

            if (_findInbound(current.Ssrc) is { } track)
                streams.Add(new InboundStreamSample(track.UserId, track.SourceType, track.MediaType,
                    Math.Max(0, delta.Packets), LossPercent(delta), current.JitterMs, delta.Plis));
        }

        var inboundLoss = received + lost > 0 ? 100.0 * lost / (received + lost) : 0;

        // Only the streams that still carry packets: the SFU repeats its last report of a stopped one
        var reports = outbound
            .Where(stream => stream.Packets > 0 && _previous.TryGetValue(stream.Ssrc, out var last)
                                                && stream.Packets > last.Packets)
            .Select(stream => remote.GetValueOrDefault(stream.Ssrc))
            .OfType<RemoteReport>()
            .ToList();

        // A voice tells nothing of our own loss, see TransportStatsSample.OutboundLossPercent
        var outboundLoss = reports
            .Where(report => _findOutbound(report.Ssrc)?.MediaType != MediaType.Audio)
            .Select(report => report.FractionLost).DefaultIfEmpty(0).Max() * 100;
        var roundTrips = reports.Where(report => report.RoundTripMs > 0).Select(report => report.RoundTripMs).ToList();

        return new TransportStatsSample(roundTrips.Count > 0 ? roundTrips.Average() : null, inboundLoss, outboundLoss,
            streams, _isOverTcp);
    }

    private static double LossPercent(Counters delta)
    {
        var expected = delta.Packets + Math.Max(0, delta.Lost);

        return expected > 0 ? 100.0 * Math.Max(0, delta.Lost) / expected : 0;
    }

    private void LogInbound(LogLevel level, Counters current, Counters delta, double seconds)
    {
        var track = _findInbound(current.Ssrc);

        LogInboundStats(level, _core.SessionId, track?.UserId ?? 0, track?.SourceType.ToString() ?? UnknownSource,
            current.Ssrc, Kbps(delta.Bytes, seconds), delta.Packets / seconds, Math.Max(0, delta.Lost), LossPercent(delta),
            current.JitterMs, delta.Late, delta.Nacks, delta.Plis);
    }

    private void LogOutbound(LogLevel level, Counters current, Counters delta, double seconds, RemoteReport? remote)
    {
        var track = _findOutbound(current.Ssrc);

        LogOutboundStats(level, _core.SessionId, track?.SourceType.ToString() ?? UnknownSource, current.Ssrc,
            Kbps(delta.Bytes, seconds), delta.Packets / seconds, (remote?.FractionLost ?? 0) * 100,
            remote?.JitterMs ?? 0, remote?.RoundTripMs ?? 0, delta.Nacks, delta.Plis);
    }

    private static double Kbps(long bytes, double seconds) => bytes * 8 / seconds / 1000;

    private Counters ReadInbound(Structure stats)
    {
        stats.GetUint("ssrc", out var ssrc);
        stats.GetUint64("packets-received", out var packets);
        stats.GetUint64("bytes-received", out var bytes);
        stats.GetInt64("packets-lost", out var lost);
        stats.GetDouble("jitter", out var jitter);
        // Sent by us: the repairs this stream needed
        stats.GetUint("nack-count", out var nacks);
        stats.GetUint("pli-count", out var plis);

        // Packets that came after the jitter buffer had given them up
        ulong late = 0;

        if (stats.HasField("gst-rtpjitterbuffer-stats"))
        {
            using var value = stats.GetValue("gst-rtpjitterbuffer-stats");
            using var buffer = value.GetBoxed<Structure>();

            buffer?.GetUint64("num-late", out late);
        }

        // Those of the stream itself, see WebRTCBinTuning.ReadJitterBuffer
        if (_readJitterBuffer(ssrc) is { } own)
            (lost, late) = (own.Lost, (ulong)own.Late);

        return new Counters(ssrc, (long)packets, (long)bytes, lost, (long)late,
            nacks, plis, jitter * 1000);
    }

    private static Counters ReadOutbound(Structure stats)
    {
        stats.GetUint("ssrc", out var ssrc);
        stats.GetUint64("packets-sent", out var packets);
        stats.GetUint64("bytes-sent", out var bytes);
        // Received from the SFU: the repairs it asked this stream for
        stats.GetUint("nack-count", out var nacks);
        stats.GetUint("pli-count", out var plis);

        return new Counters(ssrc, (long)packets, (long)bytes, 0, 0, nacks, plis, 0);
    }

    /// <summary>
    /// What the SFU reports about a stream we send, from its receiver reports.
    /// </summary>
    private static RemoteReport ReadRemoteInbound(Structure stats)
    {
        stats.GetUint("ssrc", out var ssrc);
        stats.GetDouble("fraction-lost", out var fractionLost);
        stats.GetDouble("jitter", out var jitter);
        stats.GetDouble("round-trip-time", out var roundTrip);

        return new RemoteReport(ssrc, fractionLost, jitter * 1000, roundTrip * 1000);
    }

    /// <summary>
    /// The running counters of a stream at one reading. The jitter is the current value, not a counter.
    /// </summary>
    private readonly record struct Counters(uint Ssrc, long Packets, long Bytes, long Lost, long Late,
        uint Nacks, uint Plis, double JitterMs)
    {
        public static Counters operator -(Counters current, Counters last) => current with
        {
            Packets = current.Packets - last.Packets,
            Bytes = current.Bytes - last.Bytes,
            Lost = current.Lost - last.Lost,
            Late = current.Late - last.Late,
            Nacks = current.Nacks - last.Nacks,
            Plis = current.Plis - last.Plis
        };
    }

    private sealed record RemoteReport(uint Ssrc, double FractionLost, double JitterMs, double RoundTripMs);
}
