using Client.Tools.HardwareIdentifier;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Loader;

/// <summary>
/// Logs a summary of the system and the media runtime, for bug reports. The details go at Debug level.
/// </summary>
public static partial class GstEnvironmentReport
{
    public static void Log(ILogger logger)
    {
        var version = typeof(GstEnvironmentReport).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
#if DEBUG
        const string buildConfig = "Debug";
#else
        const string buildConfig = "Release";
#endif

        LogSummary(logger, DateTime.Now, version, buildConfig,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription);

        if (!logger.IsEnabled(LogLevel.Debug))
            return;

        string gstVersion;
        try
        {
            gstVersion = GstSharp.NativeVersion.ToString();
        }
        catch
        {
            gstVersion = "not found, the program may not work";
        }

        var gpu = GpuInfo.GetGpuInfo().FirstOrDefault();
        var plugins = Gst.Registry.Get().GetPluginList();

        LogDetails(logger,
            System.Diagnostics.Debugger.IsAttached,
            gpu == null ? "unknown" : $"{gpu.Vendor} {gpu.Name}",
            Environment.Version.ToString(),
            typeof(Gst.Element).Assembly.GetName().Version?.ToString() ?? "unknown",
            gstVersion,
            plugins.Count);

        foreach (var plugin in plugins)
        {
            if (plugin.GetFilename() is { } filename)
                LogPlugin(logger, Path.GetFileName(filename), plugin.GetVersion());
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Application loaded. Started {StartDate:yyyy-MM-dd HH:mm:ss}, assembly {Version} ({Config}), OS {OS}")]
    private static partial void LogSummary(ILogger logger, DateTime startDate, string version, string config, string os);

    [LoggerMessage(LogLevel.Debug,
        "Debugger attached: {IsDebuggerAttached}, GPU: {Gpu}, .NET {DotNetVersion}, GstSharp.Net {GstSharpVersion}, " +
        "GStreamer {GstVersion} with {PluginCount} plugins")]
    private static partial void LogDetails(ILogger logger, bool isDebuggerAttached, string gpu, string dotNetVersion,
        string gstSharpVersion, string gstVersion, int pluginCount);

    [LoggerMessage(LogLevel.Debug, "GStreamer plugin: {Plugin} | {PluginVersion}")]
    private static partial void LogPlugin(ILogger logger, string plugin, string pluginVersion);
}
