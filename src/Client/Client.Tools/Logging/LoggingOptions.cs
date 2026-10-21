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
