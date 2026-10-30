using Client.Tools.AppInfo;
using Serilog.Events;

namespace Client.Tools.Logging;

public sealed class LoggingOptions
{
#if DEBUG
    public LogEventLevel MinimumLevel { get; init; } = LogEventLevel.Debug;
#else
    public LogEventLevel MinimumLevel { get; init; } = LogEventLevel.Information;
#endif

    public bool WriteToFiles { get; init; }

    /// <summary>
    /// The size at which a file of the application is continued in a new one; the file of GStreamer
    /// may be twice as big.
    /// </summary>
    public long FileSizeLimitBytes { get; init; } = 50 * 1024 * 1024;

    /// <summary>
    /// How many files of each kind are kept, the oldest are deleted.
    /// </summary>
    public int RetainedFileCount { get; init; } = 10;

    /// <summary>
    /// ANSI colors in the console; logcat on Android shows them as garbage.
    /// </summary>
    public bool UseConsoleColors { get; init; } = true;

    public string LogDirectory { get; init; } = Path.Combine(AppDirectories.Data, "Logs");

    /// <summary>
    /// Minimum levels for categories starting with the given prefix, e.g. a namespace.
    /// </summary>
    public IReadOnlyDictionary<string, LogEventLevel> Overrides { get; init; } = new Dictionary<string, LogEventLevel>
    {
        ["Microsoft"] = LogEventLevel.Warning,
        ["System"] = LogEventLevel.Warning
    };
}
