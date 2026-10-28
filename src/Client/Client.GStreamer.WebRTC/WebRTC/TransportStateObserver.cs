using Client.Application.Ports;
using Client.GStreamer.Interop;
using Gst;
using Gst.WebRTC;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using WebRTCICEConnectionState = Gst.WebRTC.WebRTCICEConnectionState;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Follows the states webrtcbin and its ICE and DTLS transports report.
/// </summary>
/// <remarks>
/// Handlers receive the emitting object instead of reading it from the transport: the transport
/// lets go of it while the pipeline is still shutting down and notifying.
/// </remarks>
internal sealed partial class TransportStateObserver
{
    private readonly TransportCore _core;
    private readonly Action<WebRTCConnectionState> _connectionStateChanged;
    private readonly ILogger _logger;

    // The watches of the ICE and DTLS transports, see ReleaseWatches
    private readonly WrapperScope _heldWrappers = new();
    private int _areTransportsObserved;

    // The notify handlers of webrtcbin, kept to disconnect them, see Disconnect
    private readonly List<ulong> _notifications = [];

    public TransportStateObserver(TransportCore core, Action<WebRTCConnectionState> connectionStateChanged, ILogger logger)
    {
        _core = core;
        _connectionStateChanged = connectionStateChanged;
        _logger = logger;
    }

    public WebRTCConnectionState ConnectionState { get; private set; } = WebRTCConnectionState.New;

    /// <summary>
    /// State of the ICE transport of the bundled session. Watched separately from
    /// <see cref="ConnectionState"/>, which cannot tell a stalled ICE from a stalled handshake.
    /// </summary>
    public WebRTCICEConnectionState IceConnectionState { get; private set; } = WebRTCICEConnectionState.New;

    /// <summary>
    /// State of the DTLS transport of the bundled session. It stays in new or connecting until the
    /// SFU has received our fingerprint through transport.connect.
    /// </summary>
    public WebRTCDTLSTransportState DtlsTransportState { get; private set; } = WebRTCDTLSTransportState.New;

    /// <summary>
    /// Subscribes to the states of webrtcbin itself. Those of its transports follow with
    /// <see cref="ObserveTransports"/>, once there is a transceiver to reach them through.
    /// </summary>
    public void Connect(Element webrtc)
    {
        ConnectNotification(webrtc, "notify::connection-state", () => OnConnectionStateChanged(webrtc));
        ConnectNotification(webrtc, "notify::ice-gathering-state", () => OnIceGatheringStateChanged(webrtc));
        ConnectNotification(webrtc, "notify::signaling-state", () => OnSignalingStateChanged(webrtc));
    }

    /// <summary>
    /// Lets go of the watches of the ICE and DTLS transports.
    /// </summary>
    public void ReleaseWatches()
    {
        _heldWrappers.Release();
        Interlocked.Exchange(ref _areTransportsObserved, 0);
    }

    public void Disconnect(Element webrtc)
    {
        foreach (var handler in _notifications)
            webrtc.RemoveHandler(handler);

        _notifications.Clear();
    }

    /// <summary>
    /// Subscribes to the ICE and DTLS transports of the bundled session. They are reachable
    /// through the first transceiver, and with max-bundle every section shares them.
    /// </summary>
    public void ObserveTransports()
    {
        try
        {
            // Once: the transports are the same for every negotiation round
            if (Interlocked.Exchange(ref _areTransportsObserved, 1) == 1)
                return;

            using var transceiver = GstNative.EmitGetObject<Gst.GObject.Object>(_core.WebRTC!, "get-transceiver", 0);

            if (transceiver == null)
            {
                Interlocked.Exchange(ref _areTransportsObserved, 0);
                LogTransceiverUnresolved(_core.SessionId);
                return;
            }

            var sender = GstNative.GetObject(transceiver.Handle, "sender");
            var dtlsTransport = sender == IntPtr.Zero ? IntPtr.Zero : GstNative.GetObject(sender, "transport");
            var iceTransport = dtlsTransport == IntPtr.Zero ? IntPtr.Zero : GstNative.GetObject(dtlsTransport, "transport");

            GstNative.Release(sender);

            if (dtlsTransport == IntPtr.Zero || iceTransport == IntPtr.Zero)
            {
                GstNative.Release(iceTransport);
                GstNative.Release(dtlsTransport);
                Interlocked.Exchange(ref _areTransportsObserved, 0);
                LogTransportsUnresolved(_core.SessionId);
                return;
            }

            // The watches take the references over and stay for the life of the transport
            _heldWrappers.Hold(new GstPropertyWatch(dtlsTransport, "state",
                transport => _core.Notify(() => OnDtlsTransportStateChanged(transport))));
            _heldWrappers.Hold(new GstPropertyWatch(iceTransport, "state",
                transport => _core.Notify(() => OnIceTransportStateChanged(transport))));

            LogTransportStates(_core.SessionId, IceConnectionState, DtlsTransportState);
        }
        catch (Exception exception)
        {
            LogTransportStatesObserveFailed(exception, _core.SessionId);
        }
    }

    private void OnIceTransportStateChanged(IntPtr iceTransport)
    {
        var state = (WebRTCICEConnectionState)GstNative.GetInt32(iceTransport, "state");

        // The property is notified once per component, so only real transitions are reported.
        if (state == IceConnectionState)
            return;

        IceConnectionState = state;

        LogIceState(_core.SessionId, IceConnectionState);

        if (IceConnectionState == WebRTCICEConnectionState.Failed)
        {
            LogIceFailed(_core.SessionId);
        }
    }

    private void OnDtlsTransportStateChanged(IntPtr dtlsTransport)
    {
        var state = (WebRTCDTLSTransportState)GstNative.GetInt32(dtlsTransport, "state");

        if (state == DtlsTransportState)
            return;

        DtlsTransportState = state;

        LogDtlsState(_core.SessionId, DtlsTransportState);

        if (DtlsTransportState == WebRTCDTLSTransportState.Failed)
        {
            LogDtlsFailed(_core.SessionId);
        }
    }

    private void OnConnectionStateChanged(Element webrtc)
    {
        ConnectionState = (WebRTCConnectionState)GstNative.GetInt32(webrtc, "connection-state");

        LogConnectionState(_core.SessionId, ConnectionState);

        _connectionStateChanged(ConnectionState);

        switch (ConnectionState)
        {
            case WebRTCConnectionState.Connected:
                _core.Connected.TrySetResult(true);
                break;

            case WebRTCConnectionState.Failed:
            case WebRTCConnectionState.Closed:
                _core.Connected.TrySetResult(false);
                break;
        }
    }

    private void OnIceGatheringStateChanged(Element webrtc)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogIceGatheringState(_core.SessionId,
                (WebRTCICEGatheringState)GstNative.GetInt32(webrtc, "ice-gathering-state"));
        }
    }

    private void OnSignalingStateChanged(Element webrtc)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogSignalingState(_core.SessionId, (WebRTCSignalingState)GstNative.GetInt32(webrtc, "signaling-state"));
        }
    }

    private void ConnectNotification(Element webrtc, string signal, Action handler)
    {
        _notifications.Add(webrtc.ConnectSignal(signal, (_, _) =>
        {
            _core.Notify(handler);
            return null;
        }, false));
    }
}
