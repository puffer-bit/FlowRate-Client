using System.Globalization;
using System.Runtime.InteropServices;
using Client.Tools.AppInfo;
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

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{ShortContext}] {Message:l}{NewLine}{Exception}";

    private const string FileTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:l}{NewLine}{Exception}";

    /// <summary>
    /// Controls the minimum level at run time.
    /// </summary>
    public static LoggingLevelSwitch LevelSwitch { get; } = new();

    /// <summary>
    /// The last events, for a view of the journal inside the application.
    /// </summary>
    public static InMemoryLog Memory { get; } = new(5000);

    /// <summary>
    /// Where the files of the journal are, or null when it is not written to files.
    /// </summary>
    public static string? LogDirectory { get; private set; }

    public static Serilog.Core.Logger CreateSerilogLogger(LoggingOptions options)
    {
        LevelSwitch.MinimumLevel = options.MinimumLevel;

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.FromLogContext()
            .Enrich.With<ShortSourceContextEnricher>()
            .WriteTo.Console(theme: options.UseConsoleColors ? AnsiConsoleTheme.Code : ConsoleTheme.None,
                outputTemplate: ConsoleTemplate)
            .WriteTo.Sink(Memory);

        foreach (var (prefix, level) in options.Overrides)
            config.MinimumLevel.Override(prefix, level);

        LogDirectory = options.WriteToFiles ? options.LogDirectory : null;

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
                    // GStreamer writes much more than the rest of the application
                    fileSizeLimitBytes: options.FileSizeLimitBytes * 2,
                    retainedFileCountLimit: options.RetainedFileCount));

            config.WriteTo.Logger(lc => lc
                .Filter.ByExcluding(isGStreamer)
                .WriteTo.File(Path.Combine(options.LogDirectory, "app-.txt"),
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: options.FileSizeLimitBytes,
                    retainedFileCountLimit: options.RetainedFileCount));
        }

        return config.CreateLogger();
    }

    /// <summary>
    /// Writes what the application runs on at the top of every start, so that a journal sent by a user
    /// tells it without asking.
    /// </summary>
    /// <param name="version">The version of the client with its build, see <c>AppInformation.GetClientBuildVersion</c>.</param>
    /// <param name="device">What the platform says of the device, e.g. the model of the phone; null on the desktop.</param>
    public static void LogStartup(string version, string? device = null)
    {
        Log.ForContext(Constants.SourceContextPropertyName, typeof(ClientLogging).FullName).Information(
            "FlowRate {Version} ({Configuration}) started on {Platform} {OsDescription} ({OsArchitecture}), process {ProcessArchitecture}, " +
            "{Runtime}, {ProcessorCount} CPUs, culture {Culture}, device {Device}, journal level {Level}, files in {LogDirectory}",
            version, BuildConfiguration, DeviceInformation.Platform, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture, RuntimeInformation.FrameworkDescription, System.Environment.ProcessorCount,
            CultureInfo.CurrentUICulture.Name is { Length: > 0 } culture ? culture : "invariant", device ?? DeviceInformation.Name, LevelSwitch.MinimumLevel, LogDirectory ?? "none");
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
