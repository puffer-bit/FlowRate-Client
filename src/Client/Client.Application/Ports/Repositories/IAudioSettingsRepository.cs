using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IAudioSettingsRepository
{
    Task<AudioSettings> GetAsync();
    Task SaveAsync(AudioSettings settings);
}
