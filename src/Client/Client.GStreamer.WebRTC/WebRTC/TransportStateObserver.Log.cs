using Gst.WebRTC;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using WebRTCICEConnectionState = Gst.WebRTC.WebRTCICEConnectionState;

namespace Client.GStreamer.WebRTC;

internal sealed partial class TransportStateObserver
{
    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Failed to resolve the transceiver, transport states will not be reported")]
    private partial void LogTransceiverUnresolved(int sessionId);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Failed to resolve the transports, their states will not be reported")]
    private partial void LogTransportsUnresolved(int sessionId);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Transport states: ICE {IceState}, DTLS {DtlsState}")]
    private partial void LogTransportStates(int sessionId, WebRTCICEConnectionState iceState,
        WebRTCDTLSTransportState dtlsState);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to observe the transport states")]
    private partial void LogTransportStatesObserveFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] ICE transport state: {IceState}")]
    private partial void LogIceState(int sessionId, WebRTCICEConnectionState iceState);

    [LoggerMessage(LogLevel.Error,
        "[Session {SessionId}] ICE failed. The SFU never answered the connectivity checks: " +
        "verify the ufrag, the password and the candidate address of the transport")]
    private partial void LogIceFailed(int sessionId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] DTLS transport state: {DtlsState}")]
    private partial void LogDtlsState(int sessionId, WebRTCDTLSTransportState dtlsState);

    [LoggerMessage(LogLevel.Error,
        "[Session {SessionId}] DTLS failed. The most likely cause is that the SFU did not receive " +
        "our fingerprint through transport.connect")]
    private partial void LogDtlsFailed(int sessionId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Peer connection state: {State}")]
    private partial void LogConnectionState(int sessionId, WebRTCConnectionState state);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] ICE gathering state: {State}")]
    private partial void LogIceGatheringState(int sessionId, object? state);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Signaling state: {State}")]
    private partial void LogSignalingState(int sessionId, object? state);
}
