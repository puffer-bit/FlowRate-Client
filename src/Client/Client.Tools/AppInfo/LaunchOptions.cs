using Serilog.Events;

namespace Client.Tools.AppInfo;

/// <summary>Overrides for this process only; never persisted to user settings.</summary>
public sealed record LaunchOptions
{
    public static LaunchOptions Current { get; private set; } = new();
    public bool Minimized { get; init; }
    public bool AllowMultipleInstances { get; init; }
    public bool NoMedia { get; init; }
    public bool SafeMode { get; init; }
    public bool NoAutoConnect { get; init; }
    public bool DisableUpdates { get; init; }
    public bool NoFileLogging { get; init; }
    public bool SoftwareRendering { get; init; }
    public bool Help { get; init; }
    public bool Version { get; init; }
    public string? Language { get; init; }
    public string? DataDirectory { get; init; }
    public LogEventLevel? LogLevel { get; init; }
    public bool PreventAutoConnect => NoAutoConnect || SafeMode;
    public bool DisableMedia => NoMedia || SafeMode;
    public string SettingsNotice => string.Join(" ", new[]
    {
        SafeMode ? "--safe-mode" : null, NoMedia ? "--no-media" : null,
        NoAutoConnect ? "--no-auto-connect" : null, DisableUpdates ? "--disable-updates" : null,
        NoFileLogging ? "--no-file-logging" : null, LogLevel.HasValue ? "--log-level" : null,
        Language != null ? "--lang" : null, SoftwareRendering ? "--software-rendering" : null
    }.Where(value => value != null));

    public static void Configure(IReadOnlyList<string> args) => Current = Parse(args);

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var options = new LaunchOptions();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var parts = args[i].Split('=', 2);
            var name = parts[0];
            if (!seen.Add(name))
                throw new ArgumentException($"Specify {name} only once.");
            string Value()
            {
                var value = parts.Length == 2 ? parts[1]
                    : ++i < args.Count && !args[i].StartsWith("--", StringComparison.Ordinal) ? args[i]
                    : throw new ArgumentException($"{name} requires a value.");
                return !string.IsNullOrWhiteSpace(value) ? value
                    : throw new ArgumentException($"{name} requires a non-empty value.");
            }
            if (parts.Length == 2 && name is not ("--data-dir" or "--log-level" or "--lang"))
                throw new ArgumentException($"{name} does not accept a value.");
            options = name switch
            {
                "--minimized" => options with { Minimized = true },
                "--allow-multiple-instances" => options with { AllowMultipleInstances = true },
                "--no-media" => options with { NoMedia = true },
                "--safe-mode" => options with { SafeMode = true },
                "--no-auto-connect" => options with { NoAutoConnect = true },
                "--disable-updates" => options with { DisableUpdates = true },
                "--no-file-logging" => options with { NoFileLogging = true },
                "--software-rendering" => options with { SoftwareRendering = true },
                "--help" or "-h" => options with { Help = true },
                "--version" => options with { Version = true },
                "--lang" => options with { Language = Value().ToLowerInvariant() },
                "--log-level" => options with { LogLevel = ParseLevel(Value()) },
                "--data-dir" => options with { DataDirectory = Value() },
                _ => throw new ArgumentException($"Unknown argument: {name}. Use --help for available options.")
            };
        }
        if (options.Language is not (null or "en" or "ru" or "es" or "ja" or "zh"))
            throw new ArgumentException("--lang must be en, ru, es, ja or zh.");
        return options;
    }

    private static LogEventLevel ParseLevel(string value) => value.ToLowerInvariant() switch
    {
        "verbose" => LogEventLevel.Verbose, "debug" => LogEventLevel.Debug,
        "information" or "info" => LogEventLevel.Information, "warning" or "warn" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error, "fatal" => LogEventLevel.Fatal,
        _ => throw new ArgumentException("--log-level must be verbose, debug, information, warning, error or fatal.")
    };

    public const string HelpText = """
        Usage: FlowRate [options]
          --data-dir PATH       Use a custom data directory
          --minimized           Start in the tray (show startup prompts when needed)
          --allow-multiple-instances
                                Allow later launches to open separate instances (set on the first process)
          --no-media            Disable voice and video media
          --safe-mode           Disable media, global hotkeys and automatic connections
          --no-auto-connect     Disable automatic connections and session restoration
          --disable-updates     Disable update checks for this launch
          --no-file-logging     Disable log files for this launch
          --log-level LEVEL     verbose, debug, information, warning, error, fatal
          --software-rendering Use software rendering
          --lang CODE           en, ru, es, ja, zh
          --help, -h            Show this help and exit
          --version             Show the version and exit
        Value options also accept --name=value. Overrides are not saved.
        """;
}
