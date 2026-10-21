using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// Tells from the RTP packets of an audio track whether someone speaks in it, for the speaking
/// indicator. A packet counts as speech by the size of its payload: opusenc with DTX keeps sending a
/// packet every 20 ms in silence too, but with one to three bytes of payload, while a speech frame
/// carries tens of bytes. This works for senders with a voice gate, which send nothing in silence,
/// and without one alike.
/// </summary>
/// <remarks>
/// The probe runs on a streaming thread for every packet, so it only parses the header and stores a
/// timestamp. Whether the track speaks is read by a timer of the transport.
/// </remarks>
internal sealed class SpeechActivityMeter
{
    // DTX and comfort noise frames carry up to three bytes, speech at 32 kbit/s about eighty
    private const int MinSpeechPayloadBytes = 10;

    // Covers the jitter between packets, so a steady talker does not flicker
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(250);

    // Speech comes every 20 ms; a lone larger frame every 400 ms is DTX refreshing the comfort noise.
    // Measured in RTP time (Opus always uses a 48 kHz clock), not by arrival: after a stall of the
    // pipeline or the network two refresh frames arrive together and would pass for speech
    private const uint MaxSpeechGapTicks = 48 * 60;

    private const int RtpHeaderBytes = 12;

    // Held so the delegate lives as long as the probe that calls it
    private readonly PadProbeCallback _callback;
    private long _lastSpeechTimestamp;
    private uint _lastLargeRtpTimestamp;
    private bool _hasLargeFrame;
    private CULong _probe;

    public SpeechActivityMeter()
    {
        _callback = OnBuffer;
    }

    /// <summary>
    /// Starts watching the packets that pass <paramref name="pad"/>, which must carry RTP.
    /// </summary>
    public void Attach(Pad pad) => _probe = pad.AddProbe(PadProbeType.Buffer, _callback);

    /// <summary>
    /// Stops watching <paramref name="pad"/>, the one given to <see cref="Attach"/>.
    /// </summary>
    public void Detach(Pad pad)
    {
        if (_probe.Value != 0)
            pad.RemoveProbe(_probe);

        _probe = default;
    }

    /// <param name="now">A <see cref="Stopwatch"/> timestamp, shared by all meters read in one round.</param>
    public bool IsSpeaking(long now)
    {
        var last = Volatile.Read(ref _lastSpeechTimestamp);

        return last != 0 && Stopwatch.GetElapsedTime(last, now) < HoldTime;
    }

    private unsafe PadProbeReturn OnBuffer(Pad pad, PadProbeInfo info)
    {
        // The wrapper holds a reference of its own, released here rather than by the GC
        using var buffer = info.GetBuffer();

        if (buffer == null || !buffer.Map(out var map, MapFlags.Read))
            return PadProbeReturn.Ok;

        try
        {
            var packet = new ReadOnlySpan<byte>((void*)map.DataPtr, (int)map.Size);

            if (GetPayloadLength(packet) >= MinSpeechPayloadBytes)
            {
                var rtpTimestamp = BinaryPrimitives.ReadUInt32BigEndian(packet[4..]);

                // Wraps around like the timestamp; a reordered packet comes out huge and is skipped
                var gap = rtpTimestamp - _lastLargeRtpTimestamp;

                if (_hasLargeFrame && gap != 0 && gap <= MaxSpeechGapTicks)
                    Volatile.Write(ref _lastSpeechTimestamp, Stopwatch.GetTimestamp());

                _lastLargeRtpTimestamp = rtpTimestamp;
                _hasLargeFrame = true;
            }
        }
        finally
        {
            buffer.Unmap(map);
        }

        return PadProbeReturn.Ok;
    }

    /// <summary>
    /// Length of the payload of an RTP packet, past the CSRC list, the header extensions and before
    /// the padding. The SFU adds header extensions, so the header length varies.
    /// </summary>
    /// <returns>The length, or 0 for anything that is not a valid RTP packet.</returns>
    internal static int GetPayloadLength(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < RtpHeaderBytes || packet[0] >> 6 != 2)
            return 0;

        var hasPadding = (packet[0] & 0x20) != 0;
        var hasExtension = (packet[0] & 0x10) != 0;
        var csrcCount = packet[0] & 0x0F;

        var headerLength = RtpHeaderBytes + csrcCount * 4;

        if (hasExtension)
        {
            if (packet.Length < headerLength + 4)
                return 0;

            var extensionWords = BinaryPrimitives.ReadUInt16BigEndian(packet[(headerLength + 2)..]);
            headerLength += 4 + extensionWords * 4;
        }

        var paddingLength = hasPadding && packet.Length > headerLength ? packet[^1] : 0;
        var payloadLength = packet.Length - headerLength - paddingLength;

        return Math.Max(payloadLength, 0);
    }
}
