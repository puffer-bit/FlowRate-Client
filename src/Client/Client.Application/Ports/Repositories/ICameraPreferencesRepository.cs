using Client.Core.Media;

namespace Client.Application.Ports.Repositories;

public interface ICameraPreferencesRepository
{
    Task<CameraPreferences> GetAsync();
    Task SaveAsync(CameraPreferences preferences);
}
