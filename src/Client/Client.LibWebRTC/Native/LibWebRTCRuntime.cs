using System.Runtime.InteropServices;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.LibWebRTC.Native;

/// <summary>
/// libwebrtc of the process: loaded once, with the audio devices it opens. The microphone is
/// shared by every peer, so its device is chosen here.
/// </summary>
public sealed partial class LibWebRTCRuntime(IAudioDeviceProvider devices, ILogger<LibWebRTCRuntime> logger)
    : ILibWebRTCLoader
{
    // libwebrtc lists the default device of the system first, under the name of the device it is now
    private const string DefaultPrefix = "default: ";

    private readonly Lock _lock = new();
    private bool? _isLoaded;
    private string? _loadError;
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

                if (_isLoaded == false)
                {
                    _loadError = "libwebrtc did not start its peer connection factory";
                    LogLoadFailed(_loadError);
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                                  or BadImageFormatException)
            {
                LogLoadFailed(exception.Message);
                _isLoaded = false;
                _loadError = exception.Message;
            }

            if (_isLoaded == true)
            {
                LogLoaded();
                SelectPlayoutDevice();
            }

            return _isLoaded.Value;
        }
    }

    public string? Load()
    {
        if (TryLoad())
            return null;

        lock (_lock)
            return _loadError;
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

            var available = ReadDevices(isRecording: true);
            var index = deviceId == null ? FindDefault(available, isRecording: true) : FindDevice(available, deviceId);

            if (index < 0)
            {
                LogDeviceMissing(deviceId!, string.Join(", ", available.Select(device => device.Name)));
                index = FindDefault(available, isRecording: true);
            }

            NativeMethods.SetRecordingDevice(index);
            _recordingDeviceId = deviceId;

            if (index < available.Count)
                LogDeviceSelected(available[index].Name);
        }
    }

    /// <summary>
    /// Plays to the default device of the system. libwebrtc left alone plays to the default one for
    /// communications on Windows, which may be another one: the voices are received, light their
    /// speaking indicators and go where nobody listens. Chosen before any peer plays.
    /// </summary>
    private void SelectPlayoutDevice()
    {
        var available = ReadDevices(isRecording: false);
        var index = FindDefault(available, isRecording: false);

        if (index >= available.Count)
            return;

        if (NativeMethods.SetPlayoutDevice(index) != 0)
            LogPlayoutDeviceSelected(available[index].Name);
        else
            LogPlayoutDeviceFailed(available[index].Name);
    }

    // The default device of the system: where libwebrtc gives its id (WASAPI), the device of that id,
    // and the first one otherwise, which is the default one of PulseAudio
    private static unsafe int FindDefault(List<(string Name, string Guid)> available, bool isRecording)
    {
        const int size = 512;
        var guid = stackalloc byte[size];

        if (NativeMethods.DefaultDevice(isRecording ? 1 : 0, guid, size) == 0)
            return 0;

        var id = Marshal.PtrToStringUTF8((nint)guid);
        var index = available.FindIndex(device => device.Guid == id);

        return index >= 0 ? index : 0;
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

    private static unsafe List<(string Name, string Guid)> ReadDevices(bool isRecording)
    {
        const int size = 512;
        var name = stackalloc byte[size];
        var guid = stackalloc byte[size];
        var devices = new List<(string, string)>();
        var count = isRecording ? NativeMethods.RecordingDeviceCount() : NativeMethods.PlayoutDeviceCount();

        for (var index = 0; index < count; index++)
        {
            var isRead = isRecording
                ? NativeMethods.RecordingDevice(index, name, size, guid, size)
                : NativeMethods.PlayoutDevice(index, name, size, guid, size);

            if (isRead == 0)
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

    [LoggerMessage(LogLevel.Information, "Speakers of libwebrtc: {Name}")]
    private partial void LogPlayoutDeviceSelected(string name);

    [LoggerMessage(LogLevel.Warning, "libwebrtc could not play to {Name}, it keeps its own default")]
    private partial void LogPlayoutDeviceFailed(string name);
}
