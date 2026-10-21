using Client.Core.Settings;

namespace Client.Application.Ports.Repositories;

public interface IVideoSettingsRepository
{
    Task<VideoSettings> GetAsync();
    Task SaveAsync(VideoSettings settings);
}
