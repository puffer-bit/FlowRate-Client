using Client.Core.Media;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

internal sealed partial class IncomingStreamPlayer
{
    [LoggerMessage(LogLevel.Error,
        "[Session {SessionId}] Failed to build the branch for {Pad}. Check GStreamer plugin availability")]
    private partial void LogBranchBuildFailed(int sessionId, string pad);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to link {Pad} to its branch: {Result}")]
    private partial void LogBranchLinkFailed(int sessionId, string pad, PadLinkReturn result);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Draining incoming stream from {Pad}, no player for it yet")]
    private partial void LogDraining(int sessionId, string pad);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Playing incoming audio from {Pad}")]
    private partial void LogPlaying(int sessionId, string pad);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Receiving incoming audio from {Pad} without playback")]
    private partial void LogReceivingSilently(int sessionId, string pad);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Decoding incoming {Codec} video from {Pad}")]
    private partial void LogDecodingVideo(int sessionId, string pad, VideoCodec codec);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Incoming stream from {Pad} is over, its branch is released")]
    private partial void LogRetired(int sessionId, string pad);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to hand over a video frame from {Pad}")]
    private partial void LogVideoFrameFailed(Exception exception, int sessionId, string pad);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to release the branch {Branch}")]
    private partial void LogReleaseFailed(Exception exception, int sessionId, string branch);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Failed to resolve the section of {Pad}, its volume cannot be changed")]
    private partial void LogMlineUnresolved(Exception? exception, int sessionId, string pad);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Volume of section {Mline} set to {Volume}")]
    private partial void LogVolumeChanged(int sessionId, uint mline, double volume);

    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] Voice of user {UserId} (section {Mline}): {Buffers} buffers, delay at the mixer " +
        "min {MinMs:F0} / avg {AverageMs:F0} / max {MaxMs:F0} / last {LastMs:F0} ms of {DeadlineMs:F0} ms")]
    private partial void LogVoiceArrival(int sessionId, int userId, uint mline, int buffers,
        double minMs, double averageMs, double maxMs, double lastMs, double deadlineMs);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Voice of user {UserId} (section {Mline}): {Late} of {Buffers} buffers came after " +
        "the mixer gave up on them, delay min {MinMs:F0} / avg {AverageMs:F0} / max {MaxMs:F0} / last {LastMs:F0} ms " +
        "of {DeadlineMs:F0} ms")]
    private partial void LogVoiceArrivedLate(int sessionId, int userId, uint mline, int late, int buffers,
        double minMs, double averageMs, double maxMs, double lastMs, double deadlineMs);
}
