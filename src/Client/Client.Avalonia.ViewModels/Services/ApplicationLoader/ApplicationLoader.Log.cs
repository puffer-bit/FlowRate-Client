using System;
using Microsoft.Extensions.Logging;

namespace Client.Services;

public partial class ApplicationLoader
{
    [LoggerMessage(LogLevel.Error, "Failed to apply the saved theme")]
    private partial void LogThemeApplyFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to apply the saved sound settings")]
    private partial void LogSoundSettingsApplyFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to apply the saved hot keys")]
    private partial void LogHotKeysApplyFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "Application loaded in {ElapsedMilliseconds} ms")]
    private partial void LogLoaded(long elapsedMilliseconds);

    [LoggerMessage(LogLevel.Error, "Failed to load the application libraries")]
    private partial void LogLibrariesLoadFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to start OpenAL, the sounds of the interface are not played")]
    private partial void LogOpenAlLoadFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to load the application settings")]
    private partial void LogSettingsLoadFailed(Exception exception);
}
