using System.Globalization;
using Client.Core.Sessions.Hybrid;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Resends the packets of our tracks the SFU reports lost, as RTX on the payload type and SSRC the
/// SFU created the producer with.
/// </summary>
/// <remarks>
/// webrtcbin has an rtprtxsend of its own, but it gives the RTX stream an SSRC of its choosing, which
/// the SFU never learns: it sees no SDP, and the producer only knows the SSRC it gave out. So the
/// description offers no RTX, which keeps the element of webrtcbin from storing anything, and this
/// one is plugged in as the aux sender behind it. The RTP session sends its retransmission requests
/// upstream, so they reach this element first, and its maps come from the tracks alone.
/// </remarks>
internal sealed partial class OutgoingRetransmission
{
    // How long a sent packet is kept for a NACK. Past a second the frame it belongs to is long late
    private const uint HistoryMs = 1000;

    // A bound besides the time: a second of a screen at about 15 Mbit/s
    private const uint HistoryPackets = 1500;

    private readonly TransportCore _core;
    private readonly ILogger _logger;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, OutboundTrack> _tracks = [];
    private Element? _sender;

    public OutgoingRetransmission(TransportCore core, ILogger logger)
    {
        _core = core;
        _logger = logger;
    }

    /// <summary>
    /// Has webrtcbin take the retransmitting element once it builds the RTP session. Called before
    /// the first negotiation.
    /// </summary>
    public void Attach(Element webrtc)
    {
        // On the thread that applies a description, while webrtcbin holds its lock: the element is
        // built and returned right away, nothing here may call back into webrtcbin
        webrtc.ConnectSignal("request-aux-sender", (_, _) => CreateSender());
    }

    /// <summary>
    /// Starts resending the lost packets of a track, if the SFU gave it an RTX stream. Called before
    /// the first packet of the track: the SSRC of the RTX stream is taken when that packet arrives.
    /// </summary>
    public void Track(OutboundTrack outbound)
    {
        if (outbound.RtxPayloadType <= 0 || outbound.RtxSSRC == 0)
            return;

        lock (_lock)
        {
            _tracks[outbound.Id] = outbound;
            ApplyMaps();
        }

        LogTrackRetransmitted(_core.SessionId, outbound.SSRC, outbound.RtxSSRC, outbound.RtxPayloadType);
    }

    public void Untrack(string outboundId)
    {
        lock (_lock)
        {
            if (_tracks.Remove(outboundId))
                ApplyMaps();
        }
    }

    /// <summary>
    /// Lets go of the element, once the pipeline has stopped. The tracks stay: the next pipeline
    /// gets them with its own element.
    /// </summary>
    public void Release()
    {
        lock (_lock)
        {
            _sender?.Dispose();
            _sender = null;
        }
    }

    private Element? CreateSender()
    {
        try
        {
            lock (_lock)
            {
                // max-bundle keeps everything in one RTP session, so there is only ever one
                if (_sender != null)
                    return null;

                var sender = ElementFactory.Make("rtprtxsend", $"retransmission_{_core.SessionId}");

                if (sender == null)
                {
                    LogSenderCreateFailed(_core.SessionId);
                    return null;
                }

                sender.SetProperty("max-size-time", HistoryMs);
                sender.SetProperty("max-size-packets", HistoryPackets);

                _sender = sender;
                ApplyMaps();

                return sender;
            }
        }
        catch (Exception exception)
        {
            LogSenderSetupFailed(exception, _core.SessionId);
            return null;
        }
    }

    /// <summary>
    /// Gives the element the payload type and SSRC of the RTX stream of every track, and the clock
    /// rate the age of a kept packet is measured in. Only inside the lock.
    /// </summary>
    private void ApplyMaps()
    {
        if (_sender == null)
            return;

        var codecs = _tracks.Values.DistinctBy(track => track.PayloadType).ToList();

        using var payloadTypeMap = BuildMap("application/x-rtp-pt-map",
            codecs.Select(track => (track.PayloadType.ToString(CultureInfo.InvariantCulture), (uint)track.RtxPayloadType)));
        using var clockRateMap = BuildMap("application/x-rtp-clock-rate-map",
            codecs.Select(track => (track.PayloadType.ToString(CultureInfo.InvariantCulture), (uint)track.ClockRate)));
        using var ssrcMap = BuildMap("application/x-rtp-ssrc-map",
            _tracks.Values.Select(track => (track.SSRC.ToString(CultureInfo.InvariantCulture), track.RtxSSRC)));

        _sender.SetProperty("clock-rate-map", clockRateMap);
        _sender.SetProperty("payload-type-map", payloadTypeMap);
        _sender.SetProperty("ssrc-map", ssrcMap);
    }

    private static Structure BuildMap(string name, IEnumerable<(string Key, uint Value)> entries) =>
        Structure.NewFromString(name + string.Concat(entries.Select(entry =>
            string.Create(CultureInfo.InvariantCulture, $", {entry.Key}=(uint){entry.Value}"))));
}
