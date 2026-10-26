using System.Runtime.InteropServices;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.LibWebRTC.Native;

/// <summary>
/// libwebrtc of the process: loaded once, with the audio devices it opens. The microphone is
/// shared by every peer, so its device is chosen here.
/// </summary>
public sealed partial class LibWebRTCRuntime(IAudioDeviceProvider devices, ILogger<LibWebRTCRuntime> logger)
{
    // libwebrtc lists the default device of the system first, under the name of the device it is now
    private const string DefaultPrefix = "default: ";

    private readonly Lock _lock = new();
    private bool? _isLoaded;
    private string? _recordingDeviceId;

    /// <summary>
    /// Loads libwebrtc, once. False when the libraries are not next to the executable, as in a
    /// build made without them.
    /// </summary>
    public bool TryLoad()
    {
        lock (_lock)
        {
            if (_isLoaded is { } isLoaded)
                return isLoaded;

            try
            {
                _isLoaded = NativeMethods.Initialize() != 0;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                                  or BadImageFormatException)
            {
                LogLoadFailed(exception.Message);
                _isLoaded = false;
            }

            if (_isLoaded == true)
                LogLoaded();

            return _isLoaded.Value;
        }
    }

    /// <summary>
    /// Captures the microphone from a device of the device list, which GStreamer makes. The two
    /// name the devices alike but give them other ids: PulseAudio lists them without any, so a
    /// device is found by its id where libwebrtc has one and by its name otherwise.
    /// </summary>
    /// <param name="deviceId">The id, or null for the default device of the system.</param>
    public void SelectRecordingDevice(string? deviceId)
    {
        lock (_lock)
        {
            if (_isLoaded != true || (deviceId == _recordingDeviceId && deviceId != null))
                return;

            var available = ReadRecordingDevices();
            var index = deviceId == null ? 0 : FindDevice(available, deviceId);

            if (index < 0)
            {
                LogDeviceMissing(deviceId!, string.Join(", ", available.Select(device => device.Name)));
                index = 0;
            }

            NativeMethods.SetRecordingDevice(index);
            _recordingDeviceId = deviceId;

            if (index < available.Count)
                LogDeviceSelected(available[index].Name);
        }
    }

    private int FindDevice(List<(string Name, string Guid)> available, string deviceId)
    {
        var index = available.FindIndex(device => device.Guid == deviceId);

        if (index >= 0)
            return index;

        var name = devices.GetInputDevices().FirstOrDefault(device => device.Id == deviceId)?.Name;

        // The default entry only while there is no other of that name: it follows the default of the
        // system, not the device
        return name == null
            ? -1
            : available.FindIndex(device => device.Name == name) is var exact and >= 0
                ? exact
                : available.FindIndex(device => device.Name == DefaultPrefix + name);
    }

    private static unsafe List<(string Name, string Guid)> ReadRecordingDevices()
    {
        const int size = 512;
        var name = stackalloc byte[size];
        var guid = stackalloc byte[size];
        var devices = new List<(string, string)>();
        var count = NativeMethods.RecordingDeviceCount();

        for (var index = 0; index < count; index++)
        {
            if (NativeMethods.RecordingDevice(index, name, size, guid, size) == 0)
                continue;

            devices.Add((Marshal.PtrToStringUTF8((nint)name) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)guid) ?? string.Empty));
        }

        return devices;
    }

    [LoggerMessage(LogLevel.Information, "libwebrtc loaded")]
    private partial void LogLoaded();

    [LoggerMessage(LogLevel.Warning, "libwebrtc is not available: {Error}")]
    private partial void LogLoadFailed(string error);

    [LoggerMessage(LogLevel.Warning, "No microphone {DeviceId} in libwebrtc, the default one is used; it has {Devices}")]
    private partial void LogDeviceMissing(string deviceId, string devices);

    [LoggerMessage(LogLevel.Information, "Microphone of libwebrtc: {Name}")]
    private partial void LogDeviceSelected(string name);
}
