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
    /// session is left. Gives the jitter buffer of every video stream the <see cref="VideoLatency"/>.
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
    /// Lets go of the RTP manager, once the pipeline has stopped.
    /// </summary>
    public void Release()
    {
        _rtpbin?.Dispose();
        _rtpbin = null;
    }
}
