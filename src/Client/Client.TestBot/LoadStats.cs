using Microsoft.Extensions.Logging;

namespace Client.TestBot;

/// <summary>
/// Counters of all bots together, reported every few seconds so that a run of many bots can be
/// read without going through the log of each one.
/// </summary>
internal sealed partial class LoadStats(ILogger<LoadStats> logger)
{
    private int _connected;
    private int _joined;
    private int _voiceStarted;
    private int _screensShared;
    private int _failedBots;
    private long _chatSent;
    private long _chatFailed;
    private long _chatReceived;
    private long _videoFrames;
    private long _connectMilliseconds;
    private long _joinMilliseconds;

    // Delivery times of the chat messages received since the last report
    private readonly List<double> _chatLatencies = [];
    private readonly Lock _chatLatenciesLock = new();

    // Times from drawing to decoding of the stamped video frames received since the last report
    private readonly List<double> _videoLatencies = [];
    private readonly Lock _videoLatenciesLock = new();

    public void Connected(TimeSpan took)
    {
        Interlocked.Increment(ref _connected);
        Interlocked.Add(ref _connectMilliseconds, (long)took.TotalMilliseconds);
    }

    public void Disconnected() => Interlocked.Decrement(ref _connected);

    public void Joined(TimeSpan took)
    {
        Interlocked.Increment(ref _joined);
        Interlocked.Add(ref _joinMilliseconds, (long)took.TotalMilliseconds);
    }

    public void Left() => Interlocked.Decrement(ref _joined);

    public void VoiceStarted() => Interlocked.Increment(ref _voiceStarted);

    public void VoiceStopped() => Interlocked.Decrement(ref _voiceStarted);

    public void ScreenShared(bool isShared)
    {
        if (isShared)
            Interlocked.Increment(ref _screensShared);
        else
            Interlocked.Decrement(ref _screensShared);
    }

    public void BotFailed() => Interlocked.Increment(ref _failedBots);

    public void ChatSent(bool isSuccess) => Interlocked.Increment(ref isSuccess ? ref _chatSent : ref _chatFailed);

    public void ChatReceived(TimeSpan latency)
    {
        Interlocked.Increment(ref _chatReceived);

        lock (_chatLatenciesLock)
            _chatLatencies.Add(latency.TotalMilliseconds);
    }

    /// <param name="latency">Milliseconds from drawing the test picture, when the frame carried its stamp.</param>
    public void VideoFrame(double? latency)
    {
        Interlocked.Increment(ref _videoFrames);

        if (latency is { } milliseconds)
            lock (_videoLatenciesLock)
                _videoLatencies.Add(milliseconds);
    }

    /// <summary>
    /// Writes a report every <paramref name="interval"/> until <paramref name="cancellationToken"/> fires,
    /// and a last one after that.
    /// </summary>
    public async Task ReportForeverAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        long lastSent = 0, lastFailed = 0, lastReceived = 0, lastFrames = 0;

        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);
                Report();
            }
        }
        catch (OperationCanceledException)
        {
            Report();
        }

        void Report()
        {
            var sent = Interlocked.Read(ref _chatSent);
            var failed = Interlocked.Read(ref _chatFailed);
            var received = Interlocked.Read(ref _chatReceived);
            var frames = Interlocked.Read(ref _videoFrames);
            var connected = Volatile.Read(ref _connected);
            var joined = Volatile.Read(ref _joined);

            double[] latencies;

            lock (_chatLatenciesLock)
            {
                latencies = _chatLatencies.Order().ToArray();
                _chatLatencies.Clear();
            }

            var seconds = interval.TotalSeconds;

            LogReport(connected, joined, Volatile.Read(ref _voiceStarted), Volatile.Read(ref _screensShared),
                Volatile.Read(ref _failedBots),
                Average(Interlocked.Read(ref _connectMilliseconds), connected),
                Average(Interlocked.Read(ref _joinMilliseconds), joined));

            LogChatReport(sent, failed, received,
                (sent - lastSent) / seconds, (failed - lastFailed) / seconds, (received - lastReceived) / seconds,
                Percentile(latencies, 0.5), Percentile(latencies, 0.95),
                latencies.Length == 0 ? 0 : latencies[^1]);

            double[] videoLatencies;

            lock (_videoLatenciesLock)
            {
                videoLatencies = _videoLatencies.Order().ToArray();
                _videoLatencies.Clear();
            }

            LogVideoReport((frames - lastFrames) / seconds,
                Percentile(videoLatencies, 0.5), Percentile(videoLatencies, 0.95),
                videoLatencies.Length == 0 ? 0 : videoLatencies[^1]);

            (lastSent, lastFailed, lastReceived, lastFrames) = (sent, failed, received, frames);
        }
    }

    private static long Average(long total, int count) => count <= 0 ? 0 : total / count;

    public static double Percentile(double[] sorted, double percentile) =>
        sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(percentile * sorted.Length) - 1)];

    [LoggerMessage(LogLevel.Information,
        "[stats] bots: {Connected} connected, {Joined} joined, {Voice} speaking, {Screens} sharing, " +
        "{Failed} failed; connect {ConnectMs} ms, join {JoinMs} ms on average")]
    private partial void LogReport(int connected, int joined, int voice, int screens, int failed,
        long connectMs, long joinMs);

    [LoggerMessage(LogLevel.Information,
        "[stats] chat: {Sent} sent, {Failed} failed, {Received} received " +
        "({SentRate:0.#}/s, {FailedRate:0.#}/s, {ReceivedRate:0.#}/s); " +
        "delivery p50 {P50:0} ms, p95 {P95:0} ms, max {Max:0} ms")]
    private partial void LogChatReport(long sent, long failed, long received, double sentRate, double failedRate,
        double receivedRate, double p50, double p95, double max);

    [LoggerMessage(LogLevel.Information,
        "[stats] video: {Fps:0.#} frames/s received by all bots; latency p50 {P50:0} ms, p95 {P95:0} ms, max {Max:0} ms")]
    private partial void LogVideoReport(double fps, double p50, double p95, double max);
}
