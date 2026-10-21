using System;
using Microsoft.Extensions.Logging;

namespace Client.Services.ScreenCast.Linux;

public partial class XdgDesktopPortalClient
{
    [LoggerMessage(LogLevel.Debug, "Portal session created: {SessionPath}")]
    private partial void LogSessionCreated(string sessionPath);

    [LoggerMessage(LogLevel.Information,
        "Portal granted a screen cast stream on PipeWire node {NodeId}, {Width}x{Height}")]
    private partial void LogStreamGranted(uint nodeId, int width, int height);

    [LoggerMessage(LogLevel.Debug, "The user cancelled the portal dialog at {Method}")]
    private partial void LogCancelled(string method);

    [LoggerMessage(LogLevel.Information, "The desktop closed the portal session {SessionPath}")]
    private partial void LogSessionClosedByDesktop(string sessionPath);

    [LoggerMessage(LogLevel.Warning, "Failed to close the portal session {SessionPath}")]
    private partial void LogSessionCloseFailed(Exception exception, string sessionPath);
}
