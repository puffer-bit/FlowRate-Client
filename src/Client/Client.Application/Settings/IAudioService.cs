using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Core.Media;
using Client.Core.Settings;

namespace Client.Application.Settings;

public interface IAudioService
{
    /// <inheritdoc cref="IAudioDeviceProvider.InputDevicesChanged"/>
    event Action? InputDevicesChanged;
    
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    
    /// <summary>
    /// Raised after the settings are saved, with the values as stored. Running sessions apply them
    /// right away. Raised on the thread that saved them.
    /// </summary>
    event Action<AudioSettings>? SettingsChanged;

    Task<AudioSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AudioSettings settings);
    
    /// <summary>
    /// Finds the connected device that matches the saved choice. Falls back to the saved name,
    /// because some backends change the identifier when the device is moved to another port.
    /// </summary>
    /// <returns>The device to capture from, or null to use the system default one.</returns>
    Task<AudioDeviceInfo?> ResolveInputDeviceAsync();
}
