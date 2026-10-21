using System;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Main;

public sealed partial class UpdaterVM
{
    [LoggerMessage(LogLevel.Warning, "Could not check for updates")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "Update {Version} is available, running {CurrentVersion}")]
    private partial void LogUpdateFound(string version, string? currentVersion);

    [LoggerMessage(LogLevel.Warning, "Could not download update {Version}")]
    private partial void LogDownloadFailed(Exception exception, string version);

    [LoggerMessage(LogLevel.Error, "Could not apply update {Version}")]
    private partial void LogApplyFailed(Exception exception, string version);
}
