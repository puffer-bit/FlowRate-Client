using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Loader;

public partial class GstLoader
{
    [LoggerMessage(LogLevel.Error, "GStreamer runtime not found in {Directory}. The application package is incomplete")]
    private static partial void LogRuntimeNotFound(ILogger logger, string directory);

    [LoggerMessage(LogLevel.Error, "No GStreamer is installed on the system")]
    private static partial void LogInstalledRuntimeNotFound(ILogger logger);

    [LoggerMessage(LogLevel.Information, "{Version} initialized from {Directory}")]
    private static partial void LogInitialized(ILogger logger, string version, string? directory);

    [LoggerMessage(LogLevel.Error, "Failed to initialize GStreamer")]
    private static partial void LogInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "VA-API plugin build: {Folder}")]
    private static partial void LogVaPluginBuild(ILogger logger, string folder);

    [LoggerMessage(LogLevel.Information, "PipeWire plugin build: {Folder} (libpipewire {Version})")]
    private static partial void LogPipeWirePluginBuild(ILogger logger, string folder, string version);
}
