using Client.Core.Sessions.Hybrid;
using Client.GStreamer.Interop;
using Shared.Models.Media;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Settings of the objects inside webrtcbin that it has no properties of its own for.
/// </summary>
internal sealed partial class WebRTCBinTuning
{
    /// <summary>
    /// The jitter buffer of a video stream whose track names none, in ms. webrtcbin gives every stream the one of the voices,
    /// which also bounds the time a NACKed packet has to come back in: past a round trip of about
    /// that much to the SFU, a lost packet of a screen breaks every frame up to the next keyframe.
    /// Past the first packets of a stream, which wait for it once, packets that come in order pass
    /// at once, so it only delays the picture while a loss is being repaired; and the video sinks do
    /// not sync, so the voices are not delayed with it.
    /// </summary>
    private const uint VideoLatency = 200;

    /// <summary>
    /// The RTCP bandwidth of the session, in bytes per second, a quarter of it for the senders. As
    /// much as the default 5% of a screen at 8 Mbit/s, and with room for the reports of a full room.
    /// </summary>
    private const int RtcpBudget = 50_000;

    /// <summary>
    /// The send buffer of the socket the media goes over, in bytes: a keyframe of a screen at
    /// 20 Mbit/s and its retransmissions. Windows gives a UDP socket far less, and a full one stalled
    /// the sending for good, see <see cref="WakeSender"/>. Linux caps it at net.core.wmem_max.
    /// </summary>
    private const int SendBufferBytes = 4 * 1024 * 1024;

    private readonly TransportCore _core;
    private readonly ILogger _logger;

    // Held while the transport lives: disposing the wrapper disconnects the handler
    private Bin? _rtpbin;

    // The jitter buffer of every stream we receive, by SSRC, for its statistics
    private readonly Dictionary<uint, Element> _jitterBuffers = [];
    private readonly Lock _jitterBuffersLock = new();

    public WebRTCBinTuning(TransportCore core, ILogger logger)
    {
        _core = core;
        _logger = logger;
    }

    /// <summary>
    /// Makes the ICE agent keep the selected pair alive with binding requests instead of binding
    /// indications. mediasoup only counts requests as consent: with indications alone it declares
    /// the transport disconnected after its consent timeout (30 seconds by default) and drops the
    /// tuple, even though media and keepalives are still flowing.
    /// </summary>
    /// <remarks>
    /// consent-freshness would be the standard way to get the same behaviour, but it is a
    /// construct-only property of the agent webrtcbin has already created.
    /// </remarks>
    public void EnableConnectivityCheckKeepalives()
    {
        try
        {
            var iceAgent = GstNative.GetObject(_core.WebRTC!.Handle, "ice-agent");
            var niceAgent = iceAgent == IntPtr.Zero ? IntPtr.Zero : GstNative.GetObject(iceAgent, "agent");

            try
            {
                if (niceAgent == IntPtr.Zero)
                {
                    LogIceAgentUnreachable(_core.SessionId);
                    return;
                }

                GstNative.SetBoolean(niceAgent, "keepalive-conncheck", true);
                LogIceKeepalive(_core.SessionId, true);
            }
            finally
            {
                GstNative.Release(niceAgent);
                GstNative.Release(iceAgent);
            }
        }
        catch (Exception exception)
        {
            LogIceKeepaliveFailed(exception, _core.SessionId);
        }
    }

    /// <summary>
    /// Gives the socket of the selected candidate pair <see cref="SendBufferBytes"/>. Called whenever
    /// ICE picks a pair, as each may be another socket.
    /// </summary>
    public void WidenSendBuffer()
    {
        try
        {
            WithNiceAgent(agent =>
            {
                if (!NiceNative.TryGetSelectedSocket(agent, out _, out var socket))
                    return;

                try
                {
                    LogSendBuffer(_core.SessionId, SendBufferBytes, NiceNative.SetSendBuffer(socket, SendBufferBytes));
                }
                finally
                {
                    GstNative.Release(socket);
                }
            });
        }
        catch (Exception exception)
        {
            LogSendBufferFailed(exception, _core.SessionId);
        }
    }

    /// <summary>
    /// Wakes the sink of the transport, which may wait for good for a socket that took data again
    /// long ago. Called when nothing we send leaves for a while.
    /// </summary>
    /// <remarks>
    /// nicesink (libnice 0.1.24) waits for reliable-transport-writable whenever the socket reports it
    /// is full, even over UDP. That signal follows FD_WRITE on Windows, which the system posts once
    /// after a send failed, and GLib (2.82) clears its record of FD_WRITE only after WSASendTo
    /// returned: a receive of the same socket on another thread may take the event in between, then
    /// the send clears it, and nothing is sent again to bring a new one. All we send waited behind
    /// the sink until the session was left, while what we receive kept coming. A wake for nothing
    /// costs only a retry of the send.
    /// </remarks>
    /// <returns>The sink was woken.</returns>
    public bool WakeSender()
    {
        try
        {
            var isWoken = false;

            WithNiceAgent(agent =>
            {
                if (!NiceNative.TryGetSelectedSocket(agent, out var streamId, out var socket))
                    return;

                GstNative.Release(socket);
                NiceNative.SignalWritable(agent, streamId);
                isWoken = true;
            });

            return isWoken;
        }
        catch (Exception exception)
        {
            LogWakeSenderFailed(exception, _core.SessionId);
            return false;
        }
    }

    // The NiceAgent inside the ICE object of webrtcbin, for the length of the call
    private void WithNiceAgent(Action<IntPtr> action)
    {
        var iceAgent = GstNative.GetObject(_core.WebRTC!.Handle, "ice-agent");
        var niceAgent = iceAgent == IntPtr.Zero ? IntPtr.Zero : GstNative.GetObject(iceAgent, "agent");

        try
        {
            if (niceAgent != IntPtr.Zero)
                action(niceAgent);
        }
        finally
        {
            GstNative.Release(niceAgent);
            GstNative.Release(iceAgent);
        }
    }

    /// <summary>
    /// Lets the media we send pass webrtcbin as soon as it comes. In front of every section webrtcbin
    /// puts a clocksync that holds each packet back until the pipeline clock reaches its timestamp.
    /// The timestamps of a microphone follow its own clock, which runs off the system clock by some
    /// 100 ppm, so packets that come faster than their timestamps pile up in the last queue of the
    /// branch: 20 ms every few minutes. Once that leaky queue is full it drops the oldest packet,
    /// the clocksync then waits for one 40 ms ahead, and from then on every other packet is lost
    /// until the track is rebuilt. Sources that must be paced pace themselves, see FileAudioSource.
    /// </summary>
    public void DisableSendClockSync()
    {
        try
        {
            // While a section is linked, before its first packet
            _core.WebRTC!.ConnectSignal("element-added", (_, args) =>
            {
                if (args is [Element element] && element.GetFactory()?.GetName() == "clocksync")
                {
                    element.SetProperty("sync", false);
                    LogSendClockSyncDisabled(_core.SessionId, element.Name ?? "clocksync");
                }

                return null;
            });
        }
        catch (Exception exception)
        {
            LogSendClockSyncFailed(exception, _core.SessionId);
        }
    }

    /// <summary>
    /// Gives the jitter buffer of every stream the latency of its track and whether
    /// it asks for lost packets again, and the RTP session the <see cref="RtcpBudget"/>.
    /// </summary>
    /// <remarks>
    /// webrtcbin sets the retransmission of a new jitter buffer from do-nack of a transceiver too,
    /// but with every stream of the bundle in one session it walks all of their SSRCs and the last
    /// one wins (GStreamer 1.28 does not compare them with the SSRC of the jitter buffer). A voice
    /// without NACK then turned it off for the picture as well. Its handler runs before this one,
    /// so the value of the track stays.
    /// </remarks>
    /// <param name="findTrack">The track with the given SSRC, if it is one we receive.</param>
    public void ConfigureRtpBin(Func<uint, InboundTrack?> findTrack)
    {
        try
        {
            if ((_core.WebRTC as Bin)?.GetByName("rtpbin") is not Bin rtpbin)
                return;

            _rtpbin = rtpbin;

            // The RTP session of the bundle is made during the first negotiation, after this
            rtpbin.ConnectSignal("element-added", (_, args) =>
            {
                if (args is [Element element] && element.GetFactory()?.GetName() == "rtpsession")
                    WidenFeedbackBudget(element);

                return null;
            });

            // On a streaming thread, once the first packet of a stream arrived. The arguments are the
            // jitter buffer, the session and the SSRC
            rtpbin.ConnectSignal("new-jitterbuffer", (_, args) =>
            {
                if (args is [Element buffer, _, uint bufferSsrc])
                {
                    lock (_jitterBuffersLock)
                    {
                        _jitterBuffers.Remove(bufferSsrc, out var replaced);
                        _jitterBuffers[bufferSsrc] = buffer;
                        replaced?.Dispose();
                    }
                }

                _core.Notify(() =>
                {
                    if (args is not [Element jitterBuffer, _, uint ssrc] || findTrack(ssrc) is not { } track)
                        return;

                    jitterBuffer.SetProperty("do-retransmission", track.IsNackEnabled);

                    // A voice with DTX sends nothing for up to 400 ms, while the buffer would ask
                    // for the next packet after 20 ms, declare it lost and drop it when it came:
                    // a concealed frame and a click each time. A gap in the seqnums is still asked for.
                    if (track.MediaType == MediaType.Audio)
                        jitterBuffer.SetProperty("rtx-next-seqnum", false);

                    if (LatencyOf(track) is { } latency)
                        jitterBuffer.SetProperty("latency", latency);

                    LogJitterBuffer(_core.SessionId, ssrc, track.MediaType,
                        jitterBuffer.GetProperty<uint>("latency"), track.IsNackEnabled);
                });

                return null;
            });
        }
        catch (Exception exception)
        {
            LogRtpBinTuningFailed(exception, _core.SessionId);
        }
    }

    /// <summary>
    /// Makes the RTP manager drop the source of a track we let go of, with its jitter buffer and the
    /// thread of it: every rejoin of a peer brings a new SSRC, and the old ones would stay until the
    /// session is left. Not left to the RTP manager, which drops a source that is silent for a few
    /// seconds: a screen that pauses is, and webrtcbin forgets the section of a dropped SSRC and
    /// GStreamer (1.28) keeps its pad, so the screen came back to no section at all ("Padname ... is
    /// not unique").
    /// </summary>
    public void ForgetSource(uint ssrc)
    {
        try
        {
            lock (_jitterBuffersLock)
            {
                if (_jitterBuffers.Remove(ssrc, out var buffer))
                    buffer.Dispose();
            }

            // Every section is bundled into the session of the first one
            _rtpbin?.EmitSignal("clear-ssrc", 0u, ssrc);
            LogSourceForgotten(_core.SessionId, ssrc);
        }
        catch (Exception exception)
        {
            LogRtpBinTuningFailed(exception, _core.SessionId);
        }
    }

    /// <summary>
    /// The packets of a stream we receive that its jitter buffer gave up for lost, and those that came
    /// after it had, or null before its first packet.
    /// </summary>
    /// <remarks>
    /// The inbound-rtp statistics of webrtcbin carry these as well, but they are not to be trusted:
    /// webrtcbin hands every new jitter buffer to all the streams of the bundle (GStreamer 1.28 does
    /// not compare the SSRC, see <see cref="ConfigureRtpBin"/>), so every stream reported the figures
    /// of the last one made, a screen usually, and a voice showed the losses of a picture.
    /// </remarks>
    public (long Lost, long Late)? ReadJitterBuffer(uint ssrc)
    {
        lock (_jitterBuffersLock)
        {
            if (!_jitterBuffers.TryGetValue(ssrc, out var buffer))
                return null;

            try
            {
                using var stats = buffer.GetProperty<Structure>("stats");

                if (stats == null)
                    return null;

                stats.GetUint64("num-lost", out var lost);
                stats.GetUint64("num-late", out var late);

                return ((long)lost, (long)late);
            }
            catch (Exception exception)
            {
                LogRtpBinTuningFailed(exception, _core.SessionId);
                return null;
            }
        }
    }

    /// <summary>
    /// What the media settings gave the track, else <see cref="VideoLatency"/> for a video and the
    /// latency of webrtcbin, which is already set, for a voice.
    /// </summary>
    private static uint? LatencyOf(InboundTrack track) =>
        track.JitterBufferMs > 0 ? (uint)track.JitterBufferMs
        : track.MediaType == MediaType.Video ? VideoLatency
        : null;

    /// <summary>
    /// Gives the RTCP of the session a fixed budget instead of 5% of the bitrate it sees. A NACK goes
    /// out in RTCP, and after one sent early the next waits a whole RTCP interval (RFC 4585), which
    /// grows as the bitrate falls: on a still screen, at some tens of kbit/s, it reaches half a second,
    /// and a resent packet comes after the video jitter buffer gave it up. The budget only bounds how
    /// often reports may go out; what is sent stays a few kbit/s, mostly the feedback itself.
    /// </summary>
    private void WidenFeedbackBudget(Element session)
    {
        try
        {
            session.SetProperty("rtcp-rs-bandwidth", RtcpBudget / 4);
            session.SetProperty("rtcp-rr-bandwidth", RtcpBudget - RtcpBudget / 4);
            LogFeedbackBudget(_core.SessionId, RtcpBudget);
        }
        catch (Exception exception)
        {
            LogRtpBinTuningFailed(exception, _core.SessionId);
        }
    }

    /// <summary>
    /// Lets go of the RTP manager, once the pipeline has stopped.
    /// </summary>
    public void Release()
    {
        lock (_jitterBuffersLock)
        {
            foreach (var buffer in _jitterBuffers.Values)
                buffer.Dispose();

            _jitterBuffers.Clear();
        }

        _rtpbin?.Dispose();
        _rtpbin = null;
    }
}
