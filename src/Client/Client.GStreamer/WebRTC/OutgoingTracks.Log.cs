using Client.Application.Ports;
using Client.Core.Settings;
using Gst;
using Microsoft.Extensions.Logging;
using Shared.Models.Media;

namespace Client.GStreamer.WebRTC;

internal sealed partial class OutgoingTracks
{
    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to create the audio source for {Input}")]
    private partial void LogAudioSourceCreateFailed(int sessionId, AudioInput input);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to follow the streams of the captured application")]
    private partial void LogAudioAppStreamsFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Error,
        "[Session {SessionId}] Failed to build the capture branch. Check GStreamer plugin availability")]
    private partial void LogCaptureBranchBuildFailed(int sessionId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Outbound {MediaType} track registered, ssrc {Ssrc}")]
    private partial void LogOutboundRegistered(int sessionId, MediaType mediaType, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Outbound track stopped")]
    private partial void LogOutboundStopped(int sessionId);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] No capture branch to apply muted = {IsMuted} to")]
    private partial void LogNoCaptureBranchToMute(int sessionId, bool isMuted);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Outbound track muted: {IsMuted}")]
    private partial void LogOutboundMuteChanged(int sessionId, bool isMuted);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to request the send pad of section {Section}")]
    private partial void LogSendPadRequestFailed(int sessionId, int section);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to link the capture branch to section {Section}: {Result}")]
    private partial void LogCaptureBranchLinkFailed(int sessionId, int section, PadLinkReturn result);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Sending audio on section {Section}")]
    private partial void LogSendingAudio(int sessionId, int section);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Sending video on section {Section}")]
    private partial void LogSendingVideo(int sessionId, int section);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to create the video source for {Input}")]
    private partial void LogVideoSourceCreateFailed(int sessionId, VideoInput input);

    [LoggerMessage(LogLevel.Information,
        "[Session {SessionId}] Encoding the outgoing video with {Encoder}, scaled on the {Scaler}")]
    private partial void LogVideoEncoder(int sessionId, string encoder, string scaler);

    [LoggerMessage(LogLevel.Information,
        "[Session {SessionId}] Capture delivers {Rate:0.0} fps of {Wanted}: {Caps}")]
    private partial void LogCaptureRate(int sessionId, double rate, int wanted, string caps);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to hand over a captured frame")]
    private partial void LogCaptureFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to hand over a preview frame")]
    private partial void LogVideoFrameFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to release the capture branch")]
    private partial void LogCaptureBranchReleaseFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Information,
        "[Session {SessionId}] Voice gate at {ThresholdDb:F0} dB, opening from {EffectiveThresholdDb:F0} dB over a " +
        "noise floor of {NoiseFloorDb:F0} dB (null: not adaptive): open {OpenPercent:F0}% of the time, opened " +
        "{Openings} times, loudest peak {LoudestDb:F0} dB, average peak while open {AverageOpenPeakDb:F0} dB, " +
        "voice heard {VoicePercent:F0}% of the time (null: no voice detection)")]
    private partial void LogVoiceGateSummary(int sessionId, double thresholdDb, double effectiveThresholdDb,
        double? noiseFloorDb, double openPercent, int openings, double loudestDb, double averageOpenPeakDb,
        double? voicePercent);

    [LoggerMessage(LogLevel.Trace, "[Session {SessionId}] Voice gate open: {IsOpen}")]
    private partial void LogVoiceGateChanged(int sessionId, bool isOpen);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Outbound volume changed to {Volume}")]
    private partial void LogOutboundVolumeChanged(int sessionId, double volume);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Outbound input changed to {Input}, noise suppression {NoiseSuppression}")]
    private partial void LogOutboundInputChanged(int sessionId, AudioInput input, NoiseSuppressionLevel noiseSuppression);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Noise suppression {Requested} needs audiornnoise, which the GStreamer runtime lacks; using {Applied}")]
    private partial void LogNoiseSuppressionReplaced(int sessionId, NoiseSuppressionLevel requested, NoiseSuppressionLevel applied);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to change the outbound input")]
    private partial void LogOutboundInputChangeFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Voice gate changed to {Options} (null: off)")]
    private partial void LogVoiceGateOptionsChanged(int sessionId, VoiceGateOptions? options);
}
