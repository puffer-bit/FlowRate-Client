using System;
using Microsoft.Extensions.Logging;

namespace Client.Services.Tray;

internal sealed partial class TrayService
{
    [LoggerMessage(LogLevel.Error, "Failed to handle the window close")]
    private partial void LogWindowCloseFailed(Exception exception);
}
