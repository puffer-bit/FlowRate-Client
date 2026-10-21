using Client.Application.Ports.Repositories;
using Client.Core.Settings;

namespace Client.Application.Settings;

public class VideoService : IVideoService
{
    private readonly IVideoSettingsRepository _settings;

    public VideoService(IVideoSettingsRepository settings)
    {
        _settings = settings;
    }

    public async Task ApplyAsync() => VideoSettings.Apply(await _settings.GetAsync());

    public Task<VideoSettings> GetSettingsAsync() => _settings.GetAsync();

    public async Task SaveSettingsAsync(VideoSettings settings)
    {
        await _settings.SaveAsync(settings);
        VideoSettings.Apply(settings);
    }
}
