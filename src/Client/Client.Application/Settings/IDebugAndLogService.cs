using Client.Core.Settings;

namespace Client.Application.Settings;

/// <summary>
/// The journal of the application and the logs of its native libraries, see the Logging and debug tab.
/// </summary>
public interface IDebugAndLogService
{
    /// <summary>
    /// Puts the saved settings into effect. Called once at start; until then the journal keeps the
    /// level of the build.
    /// </summary>
    Task ApplyAsync();

    Task<DebugSettings> GetSettingsAsync();

    /// <summary>
    /// Saves the settings and puts them into effect at once.
    /// </summary>
    Task SaveSettingsAsync(DebugSettings settings);

    /// <summary>
    /// The native libraries of this platform whose log can be passed into the journal.
    /// </summary>
    IReadOnlyCollection<NativeLogSource> NativeLogSources { get; }

    /// <summary>
    /// Where the files of the journal are, or null where the platform writes none.
    /// </summary>
    string? LogDirectory { get; }

    /// <summary>
    /// What the files of the journal take, in bytes.
    /// </summary>
    Task<long> GetLogFilesSizeAsync();

    /// <summary>
    /// Deletes the files of the journal but the ones being written now.
    /// </summary>
    Task ClearLogFilesAsync();

    /// <summary>
    /// Writes a zip for a bug report: the files of the journal, the system and the settings of this tab,
    /// and the last entries kept in memory.
    /// </summary>
    Task WriteArchiveAsync(Stream destination);
}
