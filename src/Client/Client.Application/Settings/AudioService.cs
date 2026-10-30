using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Sounds;

namespace Client.Application.Settings;

public class AudioService : IAudioService
{
    private readonly IAudioSettingsRepository _audioSettingsRepository;
    private readonly IAudioDeviceProvider _audioDeviceProvider;
    private readonly ISoundService _soundService;

    public event Action? InputDevicesChanged
    {
        add => _audioDeviceProvider.InputDevicesChanged += value;
        remove => _audioDeviceProvider.InputDevicesChanged -= value;
    }
    
    public event Action<AudioSettings>? SettingsChanged;

    public AudioService(IAudioSettingsRepository audioSettingsRepository, 
        IAudioDeviceProvider audioDeviceProvider,
        ISoundService soundService)
    {
        _soundService = soundService;
        _audioSettingsRepository = audioSettingsRepository;
        _audioDeviceProvider = audioDeviceProvider;
    }

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        return _audioDeviceProvider.GetInputDevices();
    }

    public async Task<AudioSettings> GetSettingsAsync()
    {
        return await _audioSettingsRepository.GetAsync();
    }

    public async Task SaveSettingsAsync(AudioSettings settings)
    {
        settings.InputVolume = Math.Clamp(settings.InputVolume, AudioSettings.MinInputVolume, 
            AudioSettings.MaxInputVolume);
        settings.VoiceGateThresholdDb = Math.Clamp(settings.VoiceGateThresholdDb, AudioSettings.MinVoiceGateThresholdDb,
            AudioSettings.MaxVoiceGateThresholdDb);
        settings.DisabledSounds = settings.DisabledSounds
            .Where(name => SoundFiles.TryGetEvent(name, out _))
            .Distinct()
            .ToArray();
        settings.SoundsVolume = Math.Clamp(settings.SoundsVolume, AudioSettings.MinSoundsVolume,
            AudioSettings.MaxSoundsVolume);

        if (!Enum.IsDefined(settings.NoiseSuppression))
            settings.NoiseSuppression = new AudioSettings().NoiseSuppression;
        
        await _audioSettingsRepository.SaveAsync(settings);
        _soundService.Apply(settings);
        SettingsChanged?.Invoke(settings);
    }

    public async Task<bool> GetUseGStreamerAsync()
    {
        return await _audioSettingsRepository.GetUseGStreamerAsync();
    }

    public async Task UpdateUseGStreamerAsync(bool useGStreamer)
    {
        await _audioSettingsRepository.UpdateUseGStreamerAsync(useGStreamer);
    }

    public async Task<AudioDeviceInfo?> ResolveInputDeviceAsync()
    {
        var settings = await _audioSettingsRepository.GetAsync();
        
        if (settings.InputDeviceId == null)
            return null;

        // The first read starts the device monitor and waits for it, which is not for the caller's thread
        var devices = await Task.Run(_audioDeviceProvider.GetInputDevices);

        return devices.FirstOrDefault(d => d.Id == settings.InputDeviceId) 
               ?? devices.FirstOrDefault(d => d.Name == settings.InputDeviceName);
    }
}
