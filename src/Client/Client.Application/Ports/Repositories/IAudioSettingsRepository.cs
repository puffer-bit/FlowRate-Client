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

    /// <returns>Whether the voice and the screens of a session go over GStreamer instead of libwebrtc, an experimental fallback.</returns>
    Task<bool> GetUseGStreamerAsync();
    Task UpdateUseGStreamerAsync(bool useGStreamer);
}
