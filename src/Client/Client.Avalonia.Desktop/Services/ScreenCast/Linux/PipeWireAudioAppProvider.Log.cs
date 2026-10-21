using System;
using Microsoft.Extensions.Logging;

namespace Client.Services.ScreenCast.Linux;

public sealed partial class PipeWireAudioAppProvider
{
    [LoggerMessage(LogLevel.Warning, "{Tool} is not installed, the sound of a single application cannot be shared")]
    private partial void LogToolMissing(Exception exception, string tool);

    [LoggerMessage(LogLevel.Warning, "{Tool} did not answer in time")]
    private partial void LogToolTimedOut(string tool);

    [LoggerMessage(LogLevel.Error, "Failed to read the applications that play sound")]
    private partial void LogReadFailed(Exception exception);
}
