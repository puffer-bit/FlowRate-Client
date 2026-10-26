using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.SystemConsole.Themes;

namespace Client.Tools.Logging;

/// <summary>
/// Builds the Serilog pipeline in code (no configuration binding, so it stays NativeAOT-safe)
/// and plugs it into Microsoft.Extensions.Logging.
/// </summary>
public static class ClientLogging
{
    // Every category of the GStreamer project goes to a file of its own
    private const string GStreamerCategoryPrefix = "Client.GStreamer";

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{ShortContext}] {Message:lj}{NewLine}{Exception}";

    private const string FileTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Controls the minimum level at run time.
    /// </summary>
    public static LoggingLevelSwitch LevelSwitch { get; } = new();

    public static Serilog.Core.Logger CreateSerilogLogger(LoggingOptions options)
    {
        LevelSwitch.MinimumLevel = options.MinimumLevel;

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.FromLogContext()
            .Enrich.With<ShortSourceContextEnricher>()
            .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate: ConsoleTemplate);

        foreach (var (prefix, level) in options.Overrides)
            config.MinimumLevel.Override(prefix, level);

        if (options.WriteToFiles)
        {
            var isGStreamer = Matching.FromSource(GStreamerCategoryPrefix);

            // Sub-loggers: a filter on the root configuration would drop the events from every sink
            config.WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(isGStreamer)
                .WriteTo.File(Path.Combine(options.LogDirectory, "gstreamer-.txt"),
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 100 * 1024 * 1024,
                    retainedFileCountLimit: 10));

            config.WriteTo.Logger(lc => lc
                .Filter.ByExcluding(isGStreamer)
                .WriteTo.File(Path.Combine(options.LogDirectory, "app-.txt"),
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 50 * 1024 * 1024,
                    retainedFileCountLimit: 10));
        }

        return config.CreateLogger();
    }

    /// <summary>
    /// Writes the exception that brings the process down to the static <see cref="Log.Logger"/>
    /// and flushes it, so that the reason of a crash stays in the log.
    /// </summary>
    public static void LogUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception, terminating: {Terminating}",
                e.IsTerminating);
            if (e.IsTerminating)
                Log.CloseAndFlush();
        };

        // Does not end the process, but the error would be lost otherwise
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Log.Error(e.Exception, "Unobserved task exception");
    }

    /// <remarks>The caller owns the logger: flush it with <c>Log.CloseAndFlush</c> on exit.</remarks>
    public static IServiceCollection AddClientLogging(this IServiceCollection services, Serilog.ILogger logger)
    {
        return services.AddLogging(builder => builder
            .ClearProviders()
            // Below the switch level the provider drops the event anyway; let it decide
            .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace)
            .AddSerilog(logger));
    }
}
