using Client.Core.Media;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Video;

internal sealed partial class GstVideoDecoder
{
    [LoggerMessage(LogLevel.Information, "[Decoder {Name}] Decoding: {Chain}, frames as {Kind}")]
    private static partial void LogDecoding(ILogger logger, string name, string chain, VideoOutputKind kind);

    [LoggerMessage(LogLevel.Error, "[Decoder {Name}] No decoder of {Codec} in the runtime")]
    private static partial void LogNoDecoder(ILogger logger, string name, VideoCodec codec);

    [LoggerMessage(LogLevel.Error, "[Decoder {Name}] Failed to build the decoding pipeline: an element is missing")]
    private static partial void LogBuildFailed(ILogger logger, string name);

    [LoggerMessage(LogLevel.Error, "[Decoder {Name}] Failed to start the pipeline")]
    private static partial void LogStartFailed(ILogger logger, string name);

    [LoggerMessage(LogLevel.Warning, "[Decoder {Name}] Failed to hand over a video frame")]
    private static partial void LogFrameFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(LogLevel.Warning,
        "[Decoder {Name}] Frames as {Kind} cannot be shown ({Reason}), decoding into memory from the next keyframe")]
    private static partial void LogSurfaceFailed(ILogger logger, string name, VideoOutputKind kind, string? reason);

    [LoggerMessage(LogLevel.Warning, "[Decoder {Name}] Failed to rebuild the decoding pipeline")]
    private static partial void LogRebuildFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(LogLevel.Warning, "[Decoder {Name}] {Type} from {Source}: {Message} ({Debug})")]
    private static partial void LogPipelineMessage(ILogger logger, string name, MessageType type, string source,
        string message, string? debug);
}
