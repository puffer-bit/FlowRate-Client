using Client.Application.Ports;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Video;

internal sealed partial class GstScreenCapture
{
    [LoggerMessage(LogLevel.Error, "[Capture {Name}] Failed to create the video source for {Input}")]
    private static partial void LogSourceCreateFailed(ILogger logger, string name, VideoInput input);

    [LoggerMessage(LogLevel.Error, "[Capture {Name}] Failed to build the encoding branch: an element is missing or cannot be linked")]
    private static partial void LogBranchBuildFailed(ILogger logger, string name);

    [LoggerMessage(LogLevel.Error, "[Capture {Name}] Failed to start the pipeline")]
    private static partial void LogStartFailed(ILogger logger, string name);

    [LoggerMessage(LogLevel.Information, "[Capture {Name}] Capturing, encoded with {Description}")]
    private static partial void LogStarted(ILogger logger, string name, string description);

    [LoggerMessage(LogLevel.Information, "[Capture {Name}] Capture delivers {Rate:0.0} fps of {Wanted}: {Caps}")]
    private static partial void LogCaptureRate(ILogger logger, string name, double rate, int wanted, string caps);

    [LoggerMessage(LogLevel.Warning, "[Capture {Name}] Failed to hand over a captured frame")]
    private static partial void LogCaptureFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(LogLevel.Warning, "[Capture {Name}] Failed to hand over an encoded frame")]
    private static partial void LogEncodedFrameFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(LogLevel.Warning, "[Capture {Name}] Failed to hand over a preview frame")]
    private static partial void LogPreviewFrameFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(LogLevel.Warning, "[Capture {Name}] {Type} from {Source}: {Message} ({Debug})")]
    private static partial void LogPipelineMessage(ILogger logger, string name, MessageType type, string source,
        string message, string? debug);
}
