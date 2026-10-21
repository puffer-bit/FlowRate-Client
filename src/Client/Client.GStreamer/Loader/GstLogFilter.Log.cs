using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Loader;

internal static partial class GstLogFilter
{
    [LoggerMessage(LogLevel.Debug, "Suppressing known PipeWire format criticals, first one: {Message}")]
    private static partial void LogSuppressing(ILogger logger, string message);
}
