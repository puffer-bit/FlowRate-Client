using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IAudioSettingsRepository
{
    Task<AudioSettings> GetAsync();
    Task SaveAsync(AudioSettings settings);

    /// <returns>Whether the voice of a session goes over libwebrtc instead of GStreamer.</returns>
    Task<bool> GetUseLibWebRTCAsync();
    Task UpdateUseLibWebRTCAsync(bool useLibWebRTC);
}
