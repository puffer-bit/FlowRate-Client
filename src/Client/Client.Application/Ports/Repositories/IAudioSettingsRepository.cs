using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IAudioSettingsRepository
{
    Task<AudioSettings> GetAsync();
    Task SaveAsync(AudioSettings settings);

    /// <returns>Whether the voice and the screens of a session go over GStreamer instead of libwebrtc, an experimental fallback.</returns>
    Task<bool> GetUseGStreamerAsync();
    Task UpdateUseGStreamerAsync(bool useGStreamer);
}
