using Microsoft.Extensions.Logging;

namespace Client.Application.Sounds;

public sealed partial class SoundService
{
    [LoggerMessage(LogLevel.Warning, "Failed to play the sound of {SoundEvent}")]
    private partial void LogPlayFailed(Exception exception, SoundEvent soundEvent);

    [LoggerMessage(LogLevel.Warning, "Failed to load the sound of {SoundEvent}")]
    private partial void LogLoadFailed(Exception exception, SoundEvent soundEvent);

    [LoggerMessage(LogLevel.Warning, "Failed to free a sound")]
    private partial void LogReleaseFailed(Exception exception);
}
