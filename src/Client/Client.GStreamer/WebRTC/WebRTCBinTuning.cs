using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Settings of the objects inside webrtcbin that it has no properties of its own for.
/// </summary>
internal sealed partial class WebRTCBinTuning
{
    private readonly TransportCore _core;
    private readonly ILogger _logger;

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
    /// session is left.
    /// </summary>
    public void EnableSourceAutoremove()
    {
        try
        {
            using var rtpbin = (_core.WebRTC as Bin)?.GetByName("rtpbin");

            rtpbin?.SetProperty("autoremove", true);
        }
        catch (Exception exception)
        {
            LogIceKeepaliveFailed(exception, _core.SessionId);
        }
    }
}
