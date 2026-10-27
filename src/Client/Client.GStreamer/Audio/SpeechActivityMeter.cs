using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// Tells from the RTP packets of an audio track whether someone speaks in it, for the speaking
/// indicator. A packet of the SILK or hybrid mode carries the voice activity flag of the encoder,
/// which the meter reads; the size alone does not tell: with FEC the encoder stays in the hybrid
/// mode and spends tens of bytes on every frame of silence too. A packet of the CELT mode has no
/// such flag and counts as speech by its size: a CELT frame of silence takes one to three bytes,
/// while a speech frame carries tens. This works for senders with a voice gate, which send silence
/// or nothing while it is closed, and without one alike.
/// </summary>
/// <remarks>
/// The probe runs on a streaming thread for every packet, so it only parses the header and stores a
/// timestamp. Whether the track speaks is read by a timer of the transport.
/// </remarks>
public sealed class SpeechActivityMeter
{
    // DTX and comfort noise frames carry up to three bytes, speech at 32 kbit/s about eighty
    private const int MinSpeechPayloadBytes = 10;

    // The TOC configurations from this one on are CELT only, the ones before SILK or hybrid
    private const int FirstCeltConfig = 16;

    // The TOC configurations before this one are SILK only, in fours of 10, 20, 40 and 60 ms; a
    // frame of 40 or 60 ms holds two or three SILK frames, a hybrid frame always one
    private const int FirstHybridConfig = 12;

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

            if (IsSpeech(GetPayload(packet)))
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
    /// Whether an Opus packet carries speech. The voice activity flags of a SILK or hybrid packet are
    /// the first symbols of its range coder, coded at even odds, so they are the leading bits of the
    /// first byte of the frame: one per SILK frame, that is one for up to 20 ms and two or three for
    /// 40 and 60 ms. Packets of several Opus frames with lengths in front of them are judged by size.
    /// </summary>
    internal static bool IsSpeech(ReadOnlySpan<byte> payload)
    {
        // Also too short to hold a frame after the TOC
        if (payload.Length < MinSpeechPayloadBytes)
            return false;

        var config = payload[0] >> 3;
        var frameCount = payload[0] & 0x03;

        if (config >= FirstCeltConfig || frameCount >= 2)
            return true;

        var silkFrames = config < FirstHybridConfig ? Math.Max(config % 4, 1) : 1;

        return payload[1] >> (8 - silkFrames) != 0;
    }

    /// <summary>
    /// The payload of an RTP packet, past the CSRC list, the header extensions and before the
    /// padding. The SFU adds header extensions, so the header length varies.
    /// </summary>
    /// <returns>The payload, empty for anything that is not a valid RTP packet.</returns>
    internal static ReadOnlySpan<byte> GetPayload(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < RtpHeaderBytes || packet[0] >> 6 != 2)
            return [];

        var hasPadding = (packet[0] & 0x20) != 0;
        var hasExtension = (packet[0] & 0x10) != 0;
        var csrcCount = packet[0] & 0x0F;

        var headerLength = RtpHeaderBytes + csrcCount * 4;

        if (hasExtension)
        {
            if (packet.Length < headerLength + 4)
                return [];

            var extensionWords = BinaryPrimitives.ReadUInt16BigEndian(packet[(headerLength + 2)..]);
            headerLength += 4 + extensionWords * 4;
        }

        var paddingLength = hasPadding && packet.Length > headerLength ? packet[^1] : 0;
        var payloadLength = packet.Length - headerLength - paddingLength;

        return payloadLength > 0 ? packet.Slice(headerLength, payloadLength) : [];
    }
}
