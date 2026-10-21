using Client.Core.Media;


namespace Client.Application.Ports;

public interface IAudioDeviceProvider
{
    /// <summary>
    /// Raised when a capture device is plugged in, removed or reconfigured. May be raised from a
    /// non-UI thread.
    /// </summary>
    event Action? InputDevicesChanged;

    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
}
