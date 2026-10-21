using Client.Core.Media;

namespace Client.Application.Ports.Repositories;

public interface IScreenShareSettingsRepository
{
    Task<ScreenShareSettings> GetAsync();
    Task SaveAsync(ScreenShareSettings settings);
}
