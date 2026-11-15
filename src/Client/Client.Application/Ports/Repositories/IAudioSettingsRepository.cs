using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IAudioSettingsRepository
{
    Task<AudioSettings> GetAsync();
    Task SaveAsync(AudioSettings settings);

    Task<double> GetChatAudioVolumeAsync();
    Task UpdateChatAudioVolumeAsync(double volume);

    Task<double> GetChatVideoVolumeAsync();
    Task UpdateChatVideoVolumeAsync(double volume);
}
