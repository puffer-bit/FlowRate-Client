using System;
using System.Collections.Generic;
using Client.Application.Ports;
using Client.Core.Media;

namespace Client.Droid.Platform.Media;

/// <summary>
/// Lists no microphones: nothing captures sound on Android yet. The audio settings need the port
/// for the sounds of the interface, which are kept with the settings of the microphone.
/// </summary>
internal sealed class NoAudioDeviceProvider : IAudioDeviceProvider
{
    public event Action? InputDevicesChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => [];
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => [];
    public event Action? OutputDevicesChanged { add { } remove { } }
    public void SetOutputDevice(string? id, string? name) { }
    public void SetOutputVolume(double volume) { }
}
