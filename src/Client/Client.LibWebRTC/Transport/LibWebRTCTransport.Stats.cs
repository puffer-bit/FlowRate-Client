using System.Globalization;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.WebRTC.Sdp;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Client.LibWebRTC.Transport;

public sealed partial class LibWebRTCTransport
{
    private void RequestStats()
    {
        IMediaPeer? peer;

        lock (_lock)
            peer = _peer;

        var isDetailed = _statsSchedule.IsDetailedRequestDue();

        try
        {
            peer?.RequestStats(isDetailed);
        }
        catch (Exception)
        {
            // Closed in the meantime
        }
    }

    private void OnStats(TransportStats stats)
    {
        UpdateSpeaking(stats.Levels);

        TransportStatsSample? sample = null;

        lock (_lock)
        {
            if (stats.Streams is { } streams && _statsSchedule.IsCountersSampleDue())
                sample = _sampler.Read(streams, FindInbound,
                    ssrc => _outbounds.Values.FirstOrDefault(outbound => outbound.SSRC == ssrc));
        }

        if (stats.Entries is { } entries && _statsSchedule.IsPerfSampleDue())
            SamplePerf(entries);

        if (sample is { } reading)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogStatsSample(_sessionId, reading.RoundTripMs ?? 0, reading.InboundLossPercent, reading.OutboundLossPercent,
                    reading.IsOverTcp, string.Join(", ", reading.Inbound?.Select(stream =>
                        $"user {stream.UserId}: {stream.Packets} packets, jitter {stream.JitterMs:F0} ms") ?? []));

            if (_logger.IsEnabled(LogLevel.Debug) && stats.Streams is { } counters)
                LogStatsCounters(_sessionId, counters.AvailableOutgoingKbps ?? 0,
                    string.Join(", ", counters.Outbound.Select(stream =>
                        $"{stream.Ssrc}: {stream.Packets} packets, target {stream.TargetKbps ?? 0:F0} kbit/s, " +
                        $"{stream.Nacks} nacks, {stream.Retransmitted} resent, {stream.KeyframeRequests} plis")),
                    string.Join(", ", counters.Inbound.Select(stream =>
                        $"{stream.Ssrc}: {stream.Packets} packets, {stream.Lost} lost, {stream.Nacks} nacks, " +
                        $"{stream.KeyframeRequests} plis, {stream.Freezes} freezes")));

            StatsSampled?.Invoke(reading);
        }

        LogGateSummary(stats.GateSummary);
    }

    /// <summary>
    /// Takes what the encoders and decoders measured together with the reading, and passes the window
    /// on. Called on the thread of the readings, one at a time.
    /// </summary>
    private void SamplePerf(IReadOnlyList<StatsEntry> entries)
    {
        Dictionary<uint, InboundTrack> inbounds;
        Dictionary<uint, OutboundTrack> outbounds;
        List<(uint Ssrc, IncomingVideoStream Stream)> streams;
        List<(uint Ssrc, OutgoingVideo Video)> videos;

        lock (_lock)
        {
            inbounds = _inbounds.Values.GroupBy(inbound => inbound.SSRC).ToDictionary(group => group.Key, group => group.Last());
            outbounds = _outbounds.Values.GroupBy(outbound => outbound.SSRC).ToDictionary(group => group.Key, group => group.Last());
            streams = _videoStreams
                .Where(stream => _inbounds.ContainsKey(stream.Key))
                .Select(stream => (_inbounds[stream.Key].SSRC, stream.Value))
                .ToList();
            videos = _outgoingVideos
                .Where(video => _outbounds.ContainsKey(video.Key))
                .Select(video => (_outbounds[video.Key].SSRC, video.Value))
                .ToList();
        }

        // Outside the lock: the decoders and the captures take locks of their own
        var decoders = streams.ToDictionary(item => item.Ssrc,
            item => (item.Stream.TakePerf(), _videoReceiver?.TakeTiming(item.Ssrc)));
        var encoders = videos.ToDictionary(item => item.Ssrc, item => item.Video.TakePerf());

        var report = _perfSampler.Read(entries, new MediaPerfSources(inbounds.GetValueOrDefault,
            outbounds.GetValueOrDefault, decoders, encoders));

        if (report != null)
            PerfSampled?.Invoke(report);
    }

    private void LogGateSummary(VoiceGateSummary? summary)
    {
        if (summary != null)
            LogVoiceGateSummary(_sessionId, summary.ThresholdDb, summary.EffectiveThresholdDb, summary.NoiseFloorDb,
                summary.OpenPercent, summary.Openings, summary.LoudestDb, summary.AverageOpenPeakDb, summary.VoicePercent);
    }

    /// <summary>
    /// Turns the levels of the sections into who speaks: the others by the sections of their voices,
    /// ourselves by the microphone.
    /// </summary>
    private void UpdateSpeaking(IReadOnlyList<VoiceLevel> levels)
    {
        List<int> speakers = [];

        lock (_lock)
        {
            foreach (var (mid, level) in levels)
            {
                if (level < SpeakingDetector.SpeakingLevel)
                    continue;

                int? userId = mid == null
                    ? _isMicrophoneMuted ? null : _outbounds.Values.FirstOrDefault()?.UserId
                    : int.TryParse(mid, CultureInfo.InvariantCulture, out var mline) && mline < _slots.Count &&
                      _slots[mline].InboundId is { } inboundId && _inbounds.TryGetValue(inboundId, out var inbound) &&
                      inbound.SourceType == MediaSourceType.Microphone
                        ? inbound.UserId
                        : null;

                if (userId is { } speaker)
                    speakers.Add(speaker);
            }
        }

        foreach (var (userId, isSpeaking) in _speaking.Update(speakers))
            SpeakingChanged?.Invoke(userId, isSpeaking);
    }

    private void ReportEveryoneSilent()
    {
        foreach (var userId in _speaking.Reset())
            SpeakingChanged?.Invoke(userId, false);
    }
}
