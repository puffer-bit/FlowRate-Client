using Client.Core.Media;

namespace Client.ViewModels.Settings.Pages;

public class AudioInputDeviceItem
{
    /// <summary>
    /// Null for the system default device.
    /// </summary>
    public AudioDeviceInfo? Device { get; }
    public string Name { get; }
    public bool IsAvailable { get; }

    public AudioInputDeviceItem(AudioDeviceInfo? device, string name, bool isAvailable)
    {
        Device = device;
        Name = name;
        IsAvailable = isAvailable;
    }
}
