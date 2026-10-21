using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Interop;

internal sealed partial class GstBusWatcher
{
    [LoggerMessage(LogLevel.Error, "Failed to handle a {MessageType} bus message")]
    private partial void LogHandlerFailed(Exception exception, MessageType messageType);
}
