using Microsoft.Extensions.Logging;

namespace Client.TestBot;

/// <summary>
/// Messages of the top-level program, which cannot declare them itself.
/// </summary>
internal static partial class ProgramLog
{
    [LoggerMessage(LogLevel.Error, "Failed to initialize GStreamer")]
    public static partial void GStreamerInitFailed(ILogger logger);

    [LoggerMessage(LogLevel.Error, "libwebrtc is not next to the executable; see Client.LibWebRTC/Build/README.md")]
    public static partial void LibWebRTCMissing(ILogger logger);

    [LoggerMessage(LogLevel.Error, "Failed to prepare the audio input: {Error}")]
    public static partial void InputFailed(ILogger logger, string error);

    [LoggerMessage(LogLevel.Information, "Starting {Count} bot(s) against {Host}:{Port}. Press Ctrl+C to stop")]
    public static partial void Starting(ILogger logger, int count, string host, int port);

    [LoggerMessage(LogLevel.Error, "{Error}")]
    public static partial void BotFailed(ILogger logger, string error);
}
