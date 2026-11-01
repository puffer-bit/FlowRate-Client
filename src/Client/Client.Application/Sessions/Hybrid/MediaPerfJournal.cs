using System.Globalization;
using System.Text;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Tools.Logging;
using Microsoft.Extensions.Logging;
using Shared.Models.Media;

namespace Client.Application.Sessions.Hybrid;

/// <summary>
/// Puts the statistics of the media of a session into the journal, as the Media statistics part of the
/// settings asks: a summary line per stream now and then, every window into the file of
/// <see cref="MediaPerfRecords"/>, and a warning when a stream runs into trouble.
/// </summary>
/// <remarks>
/// A trouble is told when it starts, again at most every <see cref="RepeatInterval"/> while it lasts,
/// and once more when it is over, so a stream that stays bad does not fill the journal.
/// Not thread-safe: the reports of a transport come one at a time.
/// </remarks>
public sealed partial class MediaPerfJournal(int sessionId, ILogger logger)
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(1);

    // Fewer measurements in a window tell nothing of a rate or a delay
    private const int MinFrames = 10;

    private const double LossPercent = 5;
    private const double ConcealedPercent = 5;
    private const double PacerMs = 100;
    private const int DecoderQueue = 10;

    // An encoder or a decoder that lets this share of what comes in through falls behind
    private const double BehindShare = 0.8;

    private readonly Dictionary<(int UserId, MediaSourceType Source, string Kind), DateTime> _troubles = [];
    private DateTime _summaryWritten = DateTime.MinValue;

    public void Add(MediaPerfReport report)
    {
        var settings = DebugSettings.Current;
        var now = DateTime.UtcNow;

        if (settings.MediaPerfJournal != MediaPerfJournalMode.Off &&
            now - _summaryWritten >= TimeSpan.FromSeconds(settings.MediaPerfSummarySeconds) * 0.95)
        {
            _summaryWritten = now;
            WriteSummary(report);
        }

        if (settings.MediaPerfJournal == MediaPerfJournalMode.Detailed)
            MediaPerfRecords.Write(MediaPerfJson.Write(sessionId, report));

        if (settings.LogMediaPerfAnomalies)
            CheckTroubles(report, now);
    }

    private void WriteSummary(MediaPerfReport report)
    {
        if (report.Network is { } network)
            LogNetworkSummary(logger, sessionId, report.Engine, Ms(network.RoundTripMs), Kbps(network.AvailableOutgoingKbps),
                Kbps(network.SentKbps), Kbps(network.ReceivedKbps), network.IsOverTcp switch
                {
                    true => "TCP",
                    false => "UDP",
                    null => "?"
                }, $"{network.LocalCandidate ?? "?"}/{network.RemoteCandidate ?? "?"}");

        foreach (var stream in report.Streams)
            LogStreamSummary(logger, sessionId, stream.IsOwn ? "sent" : "received", stream.UserId, stream.SourceType,
                Describe(stream));

        if (report.Voices.Count > 0)
            LogVoicesSummary(logger, sessionId, string.Join("; ", report.Voices.Select(voice =>
                $"user {voice.Key}: {Describe(voice.Value)}")));
    }

    /// <summary>One line of what a stream did, the parts it has in the order of its way.</summary>
    public static string Describe(StreamPerf stream)
    {
        var parts = new List<string>();

        if (stream.Encoder is { } encoder)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"encoder {encoder.Element} via {encoder.Scaler}: capture {encoder.CapturedPerSecond:F0} fps, " +
                                $"in/out {encoder.InputPerSecond:F0}/{encoder.OutputPerSecond:F0} fps, encode {Latency(encoder.EncodeMs)}, " +
                                $"{encoder.BitrateKbps:F0}/{encoder.TargetKbps} kbit/s, {encoder.KeyFrames} keyframes " +
                                $"(max {encoder.MaxKeyFrameBytes / 1024} KB, frame {encoder.AverageFrameBytes / 1024.0:F1} KB)"));

        if (stream.Send is { } send)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"send {send.BitrateKbps:F0}/{send.TargetKbps ?? 0:F0} kbit/s, {send.SentPerSecond:F0} fps, " +
                                $"pacer {send.PacerMs:F1} ms, loss {send.LossPercent ?? 0:F1}%, rtt {Ms(send.RoundTripMs)}, " +
                                $"nack/pli/fir {send.Nacks}/{send.Plis}/{send.Firs}, resent {send.Retransmitted}, " +
                                $"huge {send.HugeFrames}, limited by {send.QualityLimitation ?? "?"}") +
                      (send.QueueMs is { } queue ? string.Create(CultureInfo.InvariantCulture, $", to encoder {Timing(queue)}, skipped {send.Skipped ?? 0}") : ""));

        if (stream.Receive is { } receive)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"receive {receive.BitrateKbps:F0} kbit/s, ") +
                      (receive.Width > 0 ? string.Create(CultureInfo.InvariantCulture, $"{receive.Width}x{receive.Height}, ") : "") +
                      string.Create(CultureInfo.InvariantCulture, $"loss {receive.LossPercent:F1}%, " +
                                $"jitter {receive.JitterMs:F0} ms, buffer {receive.JitterBufferMs:F0}/{receive.JitterBufferTargetMs ?? 0:F0} ms, " +
                                $"assembly {receive.AssemblyMs:F1} ms, to decode {receive.ProcessingMs:F0} ms, " +
                                $"frames {receive.ReceivedPerSecond:F0}/{receive.DecodedPerSecond:F0} fps, dropped {receive.Dropped}, " +
                                $"freezes {receive.Freezes} ({receive.FreezeMs:F0} ms), nack/pli/fir {receive.Nacks}/{receive.Plis}/{receive.Firs}, " +
                                $"keyframes {receive.KeyFrames}") +
                      (receive.DecoderImplementation is { } implementation
                          ? string.Create(CultureInfo.InvariantCulture, $", decoded by {implementation} in {receive.DecodeMs ?? 0:F1} ms")
                          : "") +
                      (receive.WaitMs is { } wait ? $", in libwebrtc {Timing(wait)}" : "") +
                      (receive.HandoffMs is { } handoff ? $", handoff {Timing(handoff)}" : ""));

        if (stream.Decoder is { } decoder)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"decoder {decoder.Element}: in/out {decoder.InputPerSecond:F0}/{decoder.OutputPerSecond:F0} fps, " +
                                $"decode {Latency(decoder.DecodeMs)}, queue {decoder.Queued} (max {decoder.MaxQueued}), " +
                                $"dropped {decoder.Dropped}"));

        if (stream.Audio is { } audio)
            parts.Add("sound " + Describe(audio));

        return parts.Count == 0 ? "nothing measured" : string.Join(" | ", parts);
    }

    private static string Describe(AudioReceivePerf audio) =>
        string.Create(CultureInfo.InvariantCulture, $"{audio.BitrateKbps:F0} kbit/s, loss {audio.LossPercent:F1}%, jitter {audio.JitterMs:F0} ms, " +
                  $"buffer {audio.JitterBufferMs:F0}/{audio.JitterBufferTargetMs ?? 0:F0} ms, concealed {audio.ConcealedPercent:F1}%, " +
                  $"faster/slower {audio.AcceleratedPercent:F1}/{audio.DeceleratedPercent:F1}%, fec {audio.FecPackets}");

    private void CheckTroubles(MediaPerfReport report, DateTime now)
    {
        var found = new Dictionary<(int UserId, MediaSourceType Source, string Kind), string>();

        foreach (var stream in report.Streams)
        {
            foreach (var (kind, text) in Troubles(stream))
                found[(stream.UserId, stream.SourceType, kind)] = text;
        }

        foreach (var (userId, voice) in report.Voices)
        {
            if (voice.ConcealedPercent >= ConcealedPercent)
                found[(userId, MediaSourceType.Microphone, "concealed")] =
                    string.Create(CultureInfo.InvariantCulture, $"the voice is {voice.ConcealedPercent:F1}% made up for lost or late packets, loss {voice.LossPercent:F1}%, " +
                              $"buffer {voice.JitterBufferMs:F0} ms");
        }

        foreach (var (key, text) in found)
        {
            if (_troubles.TryGetValue(key, out var told) && now - told < RepeatInterval)
                continue;

            _troubles[key] = now;
            LogTrouble(logger, sessionId, key.UserId, key.Source, text);
        }

        foreach (var key in _troubles.Keys.Where(key => !found.ContainsKey(key)).ToList())
        {
            _troubles.Remove(key);
            LogTroubleOver(logger, sessionId, key.UserId, key.Source, key.Kind);
        }
    }

    private static IEnumerable<(string Kind, string Text)> Troubles(StreamPerf stream)
    {
        if (stream.Encoder is { } encoder && encoder.InputPerSecond >= MinFrames)
        {
            var budget = 1000 / encoder.InputPerSecond;

            if (encoder.EncodeMs.Count >= MinFrames && encoder.EncodeMs.P95Ms > budget)
                yield return ("encoder-slow", string.Create(CultureInfo.InvariantCulture, 
                    $"{encoder.Element} takes {encoder.EncodeMs.P95Ms:F1} ms (p95) a frame, more than the {budget:F1} ms of {encoder.InputPerSecond:F0} fps"));

            if (encoder.OutputPerSecond < encoder.InputPerSecond * BehindShare)
                yield return ("encoder-behind", string.Create(CultureInfo.InvariantCulture,
                    $"{encoder.Element} puts out {encoder.OutputPerSecond:F0} of {encoder.InputPerSecond:F0} fps"));
        }

        if (stream.Decoder is { } decoder)
        {
            if (decoder.InputPerSecond >= MinFrames && decoder.DecodeMs.Count >= MinFrames &&
                decoder.DecodeMs.P95Ms > 1000 / decoder.InputPerSecond)
                yield return ("decoder-slow", string.Create(CultureInfo.InvariantCulture,
                    $"{decoder.Element} takes {decoder.DecodeMs.P95Ms:F1} ms (p95) a frame at {decoder.InputPerSecond:F0} fps"));

            if (decoder.MaxQueued >= DecoderQueue || decoder.Dropped > 0)
                yield return ("decoder-behind", string.Create(CultureInfo.InvariantCulture,
                    $"{decoder.Element} fell behind: up to {decoder.MaxQueued} frames waited, {decoder.Dropped} dropped"));
        }

        if (stream.Receive is { } receive)
        {
            if (receive.Freezes > 0)
                yield return ("freeze", string.Create(CultureInfo.InvariantCulture,
                    $"the picture froze {receive.Freezes} times for {receive.FreezeMs:F0} ms, loss {receive.LossPercent:F1}%, " +
                    $"buffer {receive.JitterBufferMs:F0} ms"));

            if (receive.Packets >= MinFrames && receive.LossPercent >= LossPercent)
                yield return ("loss", string.Create(CultureInfo.InvariantCulture, $"{receive.LossPercent:F1}% of the packets of the picture are lost"));
        }

        if (stream.Send is { } send)
        {
            if (send.QualityLimitation == "bandwidth")
                yield return ("bandwidth", string.Create(CultureInfo.InvariantCulture,
                    $"the network holds the screen back: {send.BitrateKbps:F0} kbit/s sent of a target of {send.TargetKbps ?? 0:F0}"));

            if (send.PacerMs >= PacerMs)
                yield return ("pacer", string.Create(CultureInfo.InvariantCulture, $"packets wait {send.PacerMs:F0} ms in the pacer before they leave"));
        }

        if (stream.Audio is { } audio && audio.ConcealedPercent >= ConcealedPercent)
            yield return ("concealed", string.Create(CultureInfo.InvariantCulture,
                $"the sound of the screen is {audio.ConcealedPercent:F1}% made up for lost or late packets"));
    }

    private static string Latency(LatencyStats stats) => stats.IsEmpty
        ? "?"
        : string.Create(CultureInfo.InvariantCulture, $"{stats.P50Ms:F1}/{stats.P95Ms:F1}/{stats.MaxMs:F1} ms");

    private static string Timing(TimingStats stats) => stats.IsEmpty
        ? "?"
        : string.Create(CultureInfo.InvariantCulture, $"{stats.AverageMs:F1}/{stats.MaxMs:F1} ms");

    private static string Ms(double? value) => value is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms:F0} ms") : "?";

    private static string Kbps(double? value) => value is { } kbps ? string.Create(CultureInfo.InvariantCulture, $"{kbps:F0} kbit/s") : "?";

    [LoggerMessage(LogLevel.Information,
        "[Session {SessionId}] Media over {Engine}: rtt {RoundTrip}, estimate {Estimate}, sent {Sent}, received {Received}, over {Protocol}, candidates {Candidates}")]
    private static partial void LogNetworkSummary(ILogger logger, int sessionId, string engine, string roundTrip,
        string estimate, string sent, string received, string protocol, string candidates);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Screen {Direction} of user {UserId} ({Source}): {Summary}")]
    private static partial void LogStreamSummary(ILogger logger, int sessionId, string direction, int userId,
        MediaSourceType source, string summary);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Voices: {Summary}")]
    private static partial void LogVoicesSummary(ILogger logger, int sessionId, string summary);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Trouble with user {UserId} ({Source}): {Trouble}")]
    private static partial void LogTrouble(ILogger logger, int sessionId, int userId, MediaSourceType source, string trouble);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Trouble with user {UserId} ({Source}) is over: {Kind}")]
    private static partial void LogTroubleOver(ILogger logger, int sessionId, int userId, MediaSourceType source, string kind);
}

/// <summary>A window of the statistics of the media as one JSON object, written without reflection.</summary>
internal static class MediaPerfJson
{
    public static string Write(int sessionId, MediaPerfReport report)
    {
        using var buffer = new MemoryStream();

        using (var json = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("time", DateTimeOffset.Now);
            json.WriteNumber("session", sessionId);
            json.WriteString("engine", report.Engine);
            json.WriteNumber("windowMs", Math.Round(report.Window.TotalMilliseconds));

            if (report.Network is { } network)
            {
                json.WriteStartObject("network");
                Number(json, "rttMs", network.RoundTripMs);
                Number(json, "estimateKbps", network.AvailableOutgoingKbps);
                Number(json, "sentKbps", network.SentKbps);
                Number(json, "receivedKbps", network.ReceivedKbps);

                if (network.IsOverTcp is { } isOverTcp)
                    json.WriteBoolean("tcp", isOverTcp);

                Text(json, "local", network.LocalCandidate);
                Text(json, "remote", network.RemoteCandidate);
                json.WriteEndObject();
            }

            json.WriteStartArray("streams");

            foreach (var stream in report.Streams)
            {
                json.WriteStartObject();
                json.WriteNumber("user", stream.UserId);
                json.WriteString("source", stream.SourceType.ToString());
                json.WriteBoolean("own", stream.IsOwn);

                if (stream.Encoder is { } encoder)
                {
                    json.WriteStartObject("encoder");
                    json.WriteString("element", encoder.Element);
                    json.WriteString("scaler", encoder.Scaler);
                    Number(json, "captureFps", encoder.CapturedPerSecond);
                    Number(json, "inFps", encoder.InputPerSecond);
                    Number(json, "outFps", encoder.OutputPerSecond);
                    Latency(json, "encodeMs", encoder.EncodeMs);
                    Number(json, "kbps", encoder.BitrateKbps);
                    json.WriteNumber("targetKbps", encoder.TargetKbps);
                    json.WriteNumber("keyFrames", encoder.KeyFrames);
                    json.WriteNumber("maxKeyFrameBytes", encoder.MaxKeyFrameBytes);
                    json.WriteNumber("frameBytes", encoder.AverageFrameBytes);
                    json.WriteEndObject();
                }

                if (stream.Send is { } send)
                {
                    json.WriteStartObject("send");
                    Number(json, "kbps", send.BitrateKbps);
                    Number(json, "targetKbps", send.TargetKbps);
                    Number(json, "fps", send.SentPerSecond);
                    Number(json, "pacerMs", send.PacerMs);
                    json.WriteNumber("resent", send.Retransmitted);
                    json.WriteNumber("nack", send.Nacks);
                    json.WriteNumber("pli", send.Plis);
                    json.WriteNumber("fir", send.Firs);
                    json.WriteNumber("huge", send.HugeFrames);
                    Text(json, "limitedBy", send.QualityLimitation);
                    Number(json, "lossPercent", send.LossPercent);
                    Number(json, "rttMs", send.RoundTripMs);
                    Timing(json, "toEncoderMs", send.QueueMs);

                    if (send.Skipped is { } skipped)
                        json.WriteNumber("skipped", skipped);

                    json.WriteEndObject();
                }

                if (stream.Receive is { } receive)
                {
                    json.WriteStartObject("receive");
                    Number(json, "kbps", receive.BitrateKbps);
                    json.WriteNumber("packets", receive.Packets);
                    Number(json, "lossPercent", receive.LossPercent);
                    Number(json, "jitterMs", receive.JitterMs);
                    Number(json, "bufferMs", receive.JitterBufferMs);
                    Number(json, "bufferTargetMs", receive.JitterBufferTargetMs);
                    Number(json, "assemblyMs", receive.AssemblyMs);
                    Number(json, "toDecodeMs", receive.ProcessingMs);
                    Number(json, "receivedFps", receive.ReceivedPerSecond);
                    Number(json, "decodedFps", receive.DecodedPerSecond);
                    json.WriteNumber("dropped", receive.Dropped);
                    json.WriteNumber("freezes", receive.Freezes);
                    Number(json, "freezeMs", receive.FreezeMs);
                    json.WriteNumber("nack", receive.Nacks);
                    json.WriteNumber("pli", receive.Plis);
                    json.WriteNumber("fir", receive.Firs);
                    json.WriteNumber("keyFrames", receive.KeyFrames);
                    json.WriteNumber("width", receive.Width);
                    json.WriteNumber("height", receive.Height);
                    Text(json, "decoder", receive.DecoderImplementation);
                    Number(json, "decodeMs", receive.DecodeMs);
                    Timing(json, "inLibWebRTCMs", receive.WaitMs);
                    Timing(json, "handoffMs", receive.HandoffMs);
                    json.WriteEndObject();
                }

                if (stream.Decoder is { } decoder)
                {
                    json.WriteStartObject("decoder");
                    json.WriteString("element", decoder.Element);
                    json.WriteBoolean("hardware", decoder.IsHardware);
                    Number(json, "inFps", decoder.InputPerSecond);
                    Number(json, "outFps", decoder.OutputPerSecond);
                    Latency(json, "decodeMs", decoder.DecodeMs);
                    json.WriteNumber("queued", decoder.Queued);
                    json.WriteNumber("maxQueued", decoder.MaxQueued);
                    json.WriteNumber("dropped", decoder.Dropped);
                    json.WriteEndObject();
                }

                if (stream.Audio is { } audio)
                    Audio(json, "audio", audio);

                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartObject("voices");

            foreach (var (userId, voice) in report.Voices)
                Audio(json, userId.ToString(CultureInfo.InvariantCulture), voice);

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Audio(System.Text.Json.Utf8JsonWriter json, string name, AudioReceivePerf audio)
    {
        json.WriteStartObject(name);
        Number(json, "kbps", audio.BitrateKbps);
        Number(json, "lossPercent", audio.LossPercent);
        Number(json, "jitterMs", audio.JitterMs);
        Number(json, "bufferMs", audio.JitterBufferMs);
        Number(json, "bufferTargetMs", audio.JitterBufferTargetMs);
        Number(json, "concealedPercent", audio.ConcealedPercent);
        Number(json, "fasterPercent", audio.AcceleratedPercent);
        Number(json, "slowerPercent", audio.DeceleratedPercent);
        json.WriteNumber("fec", audio.FecPackets);
        json.WriteEndObject();
    }

    private static void Latency(System.Text.Json.Utf8JsonWriter json, string name, LatencyStats stats)
    {
        if (stats.IsEmpty)
            return;

        json.WriteStartObject(name);
        Number(json, "p50", stats.P50Ms);
        Number(json, "p95", stats.P95Ms);
        Number(json, "max", stats.MaxMs);
        json.WriteNumber("count", stats.Count);
        json.WriteEndObject();
    }

    private static void Timing(System.Text.Json.Utf8JsonWriter json, string name, TimingStats? stats)
    {
        if (stats is not { IsEmpty: false } timing)
            return;

        json.WriteStartObject(name);
        Number(json, "avg", timing.AverageMs);
        Number(json, "max", timing.MaxMs);
        json.WriteNumber("count", timing.Count);
        json.WriteEndObject();
    }

    // Two decimals are plenty, and keep a line short
    private static void Number(System.Text.Json.Utf8JsonWriter json, string name, double? value)
    {
        if (value is { } number && double.IsFinite(number))
            json.WriteNumber(name, Math.Round(number, 2));
    }

    private static void Text(System.Text.Json.Utf8JsonWriter json, string name, string? value)
    {
        if (value != null)
            json.WriteString(name, value);
    }
}
