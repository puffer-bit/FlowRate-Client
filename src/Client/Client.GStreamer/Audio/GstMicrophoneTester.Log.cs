using Client.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Audio;

public sealed partial class GstMicrophoneTester
{
    [LoggerMessage(LogLevel.Debug, "Microphone test started on '{Device}'")]
    private partial void LogStarted(string device);

    [LoggerMessage(LogLevel.Warning,
        "Noise suppression {Requested} needs audiornnoise, which the GStreamer runtime lacks; the test uses {Applied}")]
    private partial void LogNoiseSuppressionReplaced(NoiseSuppressionLevel requested, NoiseSuppressionLevel applied);

    [LoggerMessage(LogLevel.Error, "Failed to start the microphone test")]
    private partial void LogStartFailed(Exception exception);

    [LoggerMessage(LogLevel.Debug, "Microphone test stopped")]
    private partial void LogStopped();

    [LoggerMessage(LogLevel.Warning, "Failed to measure the level of a microphone test buffer")]
    private partial void LogMeterFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Microphone test failed: {Error} ({Debug})")]
    private partial void LogFailed(string error, string? debug);
}
