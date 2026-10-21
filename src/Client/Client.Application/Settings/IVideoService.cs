using Client.Core.Settings;

namespace Client.Application.Settings;

/// <summary>
/// How screens are decoded, shown and encoded.
/// </summary>
public interface IVideoService
{
    /// <summary>
    /// Puts the saved settings into effect. Called once at start, before a session is joined.
    /// </summary>
    Task ApplyAsync();

    Task<VideoSettings> GetSettingsAsync();

    /// <summary>
    /// Saves the settings and puts them into effect. Streams that are already playing keep the
    /// decoder they were built with; the way frames are shown changes at once.
    /// </summary>
    Task SaveSettingsAsync(VideoSettings settings);
}
