using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Audio;

public sealed partial class GstAudioDeviceProvider
{
    [LoggerMessage(LogLevel.Warning, "Failed to start the GStreamer device monitor")]
    private partial void LogMonitorStartFailed();

    [LoggerMessage(LogLevel.Error, "Failed to enumerate audio devices")]
    private partial void LogEnumerateFailed(Exception exception);

    [LoggerMessage(LogLevel.Debug, "Input devices changed: {MessageType}")]
    private partial void LogDevicesChanged(MessageType messageType);

    [LoggerMessage(LogLevel.Error, "Failed to refresh audio devices")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(LogLevel.Trace, "Input device '{Name}' ({DeviceClass}): id '{Id}', properties: {Properties}")]
    private static partial void LogInputDevice(ILogger logger, string name, string deviceClass, string id,
        string properties);
}
