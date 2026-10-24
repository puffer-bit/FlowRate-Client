using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Settings of the objects inside webrtcbin that it has no properties of its own for.
/// </summary>
internal sealed partial class WebRTCBinTuning
{
    /// <summary>
    /// The jitter buffer of a video stream, in ms. webrtcbin gives every stream the one of the voices,
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

    private readonly TransportCore _core;
    private readonly ILogger _logger;

    // Held while the transport lives: disposing the wrapper disconnects the handler
    private Bin? _rtpbin;

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
    /// Makes the RTP manager drop a source that timed out, with its jitter buffer and the thread
    /// of it. Every rejoin of a peer brings a new SSRC, and by default the old ones stay until the
    /// session is left. Gives the jitter buffer of every video stream the <see cref="VideoLatency"/>,
    /// and the RTP session the <see cref="RtcpBudget"/>.
    /// </summary>
    /// <param name="isVideo">Whether the stream with the given SSRC is a video track.</param>
    public void ConfigureRtpBin(Func<uint, bool> isVideo)
    {
        try
        {
            if ((_core.WebRTC as Bin)?.GetByName("rtpbin") is not Bin rtpbin)
                return;

            _rtpbin = rtpbin;
            rtpbin.SetProperty("autoremove", true);

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
                _core.Notify(() =>
                {
                    if (args is [Element jitterBuffer, _, uint ssrc] && isVideo(ssrc))
                    {
                        jitterBuffer.SetProperty("latency", VideoLatency);
                        LogVideoJitterBuffer(_core.SessionId, ssrc, VideoLatency);
                    }
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
        _rtpbin?.Dispose();
        _rtpbin = null;
    }
}
