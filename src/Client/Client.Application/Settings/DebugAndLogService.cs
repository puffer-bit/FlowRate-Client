using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Client.Tools.Logging;
using Serilog.Events;

namespace Client.Application.Settings;

public class DebugAndLogService : IDebugAndLogService
{
    private readonly IDebugSettingsRepository _settings;
    private readonly IReadOnlyList<INativeLog> _nativeLogs;

    public DebugAndLogService(IDebugSettingsRepository settings, IEnumerable<INativeLog> nativeLogs)
    {
        _settings = settings;
        _nativeLogs = nativeLogs.ToList();
        NativeLogSources = _nativeLogs.Select(log => log.Source).Distinct().ToList();
    }

    public IReadOnlyCollection<NativeLogSource> NativeLogSources { get; }

    public async Task ApplyAsync() => Apply(await _settings.GetAsync());

    public Task<DebugSettings> GetSettingsAsync() => _settings.GetAsync();

    public async Task SaveSettingsAsync(DebugSettings settings)
    {
        await _settings.SaveAsync(settings);
        Apply(settings);
    }

    private void Apply(DebugSettings settings)
    {
        settings = settings.Normalize();

        ClientLogging.LevelSwitch.MinimumLevel = settings.JournalLevel is { } level
            ? ToSerilog(level)
            : ClientLogging.DefaultLevel;
        ClientLogging.IsWritingToFiles = settings.WriteToFiles;

        foreach (var log in _nativeLogs)
            log.SetLevel(settings.LogLevelOf(log.Source));

        DebugSettings.Apply(settings);
    }

    private static LogEventLevel ToSerilog(JournalLevel level) => level switch
    {
        JournalLevel.Verbose => LogEventLevel.Verbose,
        JournalLevel.Debug => LogEventLevel.Debug,
        JournalLevel.Warning => LogEventLevel.Warning,
        JournalLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Information
    };
}
