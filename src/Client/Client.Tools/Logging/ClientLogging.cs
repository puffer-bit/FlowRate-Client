using System.Globalization;
using System.Text;
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

    /// <summary>
    /// The category of the log of GStreamer itself, written to the file of GStreamer.
    /// </summary>
    public const string GStreamerNativeCategory = "Native.GStreamer";

    /// <summary>
    /// The category of the log of libwebrtc itself, written to a file of its own.
    /// </summary>
    public const string LibWebRTCNativeCategory = "Native.LibWebRTC";

    // The native libraries choose themselves how much they say, see NativeLogLevel
    private const string NativeCategoryPrefix = "Native";

    private static volatile bool _isWritingToFiles = true;

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
    /// Where the files of the journal are, or null when the platform does not write them.
    /// </summary>
    public static string? LogDirectory { get; private set; }

    /// <summary>
    /// The level the journal starts with, before the settings are read.
    /// </summary>
    public static LogEventLevel DefaultLevel { get; } = new LoggingOptions().MinimumLevel;

    /// <summary>
    /// Whether the events go to the files at the moment; off, they still reach the console and
    /// <see cref="Memory"/>. Means nothing where <see cref="LogDirectory"/> is null.
    /// </summary>
    public static bool IsWritingToFiles
    {
        get => _isWritingToFiles;
        set => _isWritingToFiles = value;
    }

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

        config.MinimumLevel.Override(NativeCategoryPrefix, LogEventLevel.Verbose);

        LogDirectory = options.WriteToFiles ? options.LogDirectory : null;

        if (options.WriteToFiles)
        {
            var isGStreamer = Matching.FromSource(GStreamerCategoryPrefix);
            var isGStreamerNative = Matching.FromSource(GStreamerNativeCategory);
            var isLibWebRTCNative = Matching.FromSource(LibWebRTCNativeCategory);

            // Sub-loggers: a filter on the root configuration would drop the events from every sink
            config.WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => _isWritingToFiles && (isGStreamer(e) || isGStreamerNative(e)))
                .WriteTo.File(Path.Combine(options.LogDirectory, "gstreamer-.txt"),
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    // GStreamer writes much more than the rest of the application
                    fileSizeLimitBytes: options.FileSizeLimitBytes * 2,
                    retainedFileCountLimit: options.RetainedFileCount));

            config.WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => _isWritingToFiles && isLibWebRTCNative(e))
                .WriteTo.File(Path.Combine(options.LogDirectory, "libwebrtc-.txt"),
                    outputTemplate: FileTemplate,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: options.FileSizeLimitBytes * 2,
                    retainedFileCountLimit: options.RetainedFileCount));

            config.WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => _isWritingToFiles
                                             && !isGStreamer(e) && !isGStreamerNative(e) && !isLibWebRTCNative(e))
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
        var culture = CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "invariant";
        device ??= DeviceInformation.Name;

        Version = version;
        Device = device;

        Log.ForContext(Constants.SourceContextPropertyName, typeof(ClientLogging).FullName).Information(
            "FlowRate {Version} ({Configuration}) started on {Platform} {OsDescription} ({OsArchitecture}), process {ProcessArchitecture}, " +
            "{Runtime}, {ProcessorCount} CPUs, culture {Culture}, device {Device}, journal level {Level}, files in {LogDirectory}",
            version, BuildConfiguration, DeviceInformation.Platform, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture,
            RuntimeInformation.ProcessArchitecture, RuntimeInformation.FrameworkDescription, System.Environment.ProcessorCount,
            culture, device, LevelSwitch.MinimumLevel, LogDirectory ?? "none");
    }

    /// <summary>The version of the client, as <see cref="LogStartup"/> got it.</summary>
    public static string? Version { get; private set; }

    /// <summary>The device, as <see cref="LogStartup"/> got it.</summary>
    public static string? Device { get; private set; }

    /// <summary>
    /// What the application runs on and how the journal is kept, one fact a line, for a bug report.
    /// </summary>
    public static string DescribeEnvironment()
    {
        var text = new StringBuilder();

        void Line(string name, object? value) => text.Append(name).Append(": ").AppendLine(value?.ToString());

        Line("FlowRate", $"{Version ?? "unknown"} ({BuildConfiguration})");
        Line("Platform", DeviceInformation.Platform);
        Line("Device", Device ?? DeviceInformation.Name);
        Line("System", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Line("Process", RuntimeInformation.ProcessArchitecture);
        Line("Runtime", RuntimeInformation.FrameworkDescription);
        Line("CPUs", System.Environment.ProcessorCount);
        Line("Culture", CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "invariant");
        Line("Journal level", LevelSwitch.MinimumLevel);
        Line("Journal files", LogDirectory == null ? "none" : IsWritingToFiles ? LogDirectory : $"{LogDirectory} (off)");
        Line("Written", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));

        return text.ToString();
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
