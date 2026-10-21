using Gst;
using Microsoft.Extensions.Logging;
using Client.Core.Media;
using Client.Application.Ports;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Audio;

/// <summary>
/// Lists capture devices through the GStreamer device monitor, which covers PipeWire and
/// PulseAudio on Linux and WASAPI on Windows. The same device objects can later create the
/// source element for the voice pipeline, so the list shown in the settings is exactly what
/// the capture side can open.
/// </summary>
public sealed partial class GstAudioDeviceProvider : IAudioDeviceProvider, IDisposable
{
    
    // Properties that stay the same across reboots and replugging, in order of preference:
    // PipeWire node name, WASAPI endpoint id, PulseAudio/ALSA names. The order matters: PipeWire
    // also sets "device.id", but there it is a runtime object number that changes between runs.
    private static readonly string[] StableIdProperties =
    [
        "node.name",
        "device.id",
        "device.strid",
        "device.name",
        "udev.id"
    ];
    
    private const string WasapiLoopbackProperty = "wasapi2.device.loopback";

    // Guards starting, stopping and reading the device list. The bus watcher must never take it:
    // providers post their first messages while DeviceMonitor.Start() is still running, and Start()
    // waits for them.
    private readonly Lock _startLock = new();
    private DeviceMonitor? _monitor;
    private GstBusWatcher? _busWatcher;
    private volatile IReadOnlyList<AudioDeviceInfo> _inputDevices = [];
    private bool _isStarted;
    private int _isRefreshQueued;
    private readonly ILogger<GstAudioDeviceProvider> _logger;

    public event Action? InputDevicesChanged;

    public GstAudioDeviceProvider(ILogger<GstAudioDeviceProvider> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        EnsureStarted();
        return _inputDevices;
    }

    private void EnsureStarted()
    {
        lock (_startLock)
        {
            if (_isStarted)
                return;

            // Do not retry on every call if GStreamer is unusable, the list just stays empty
            _isStarted = true;

            try
            {
                _monitor = DeviceMonitor.New();
                _monitor.AddFilter("Audio/Source", null);

                // Bus watches need a GLib main loop, which the app does not run, and sync handlers would
                // run on the provider threads; see GstBusWatcher
                _busWatcher = new GstBusWatcher(_monitor.GetBus(),
                    MessageType.DeviceAdded | MessageType.DeviceRemoved | MessageType.DeviceChanged,
                    OnBusMessage, "AudioDevices", _logger);

                if (!_monitor.Start())
                {
                    LogMonitorStartFailed();
                    return;
                }

                _inputDevices = ReadDevices(_monitor, _logger);
            }
            catch (Exception exception)
            {
                LogEnumerateFailed(exception);
            }
        }
    }

    private void OnBusMessage(Message message)
    {
        LogDevicesChanged(message.Type);
        
        // The list is re-read on the thread pool, so that the watcher can drain a burst of messages
        // (e.g. a headset exposing several nodes) meanwhile and they are coalesced into one refresh.
        if (Interlocked.Exchange(ref _isRefreshQueued, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ => RefreshDevices());
    }

    private void RefreshDevices()
    {
        Volatile.Write(ref _isRefreshQueued, 0);
        
        try
        {
            // Not while the monitor is starting: a provider that has not started yet is probed on
            // the spot, also one that another provider is about to hide, so the list would get
            // devices that cannot be opened. This runs on the thread pool, not on the watcher.
            lock (_startLock)
            {
                var monitor = _monitor;

                if (monitor == null)
                    return;

                _inputDevices = ReadDevices(monitor, _logger);
            }
        }
        catch (Exception exception)
        {
            LogRefreshFailed(exception);
            return;
        }
        
        InputDevicesChanged?.Invoke();
    }

    /// <summary>
    /// Creates the source element for a device from the list.
    /// </summary>
    /// <param name="deviceId">Id from <see cref="GetInputDevices"/>, or null for the system default device.</param>
    /// <returns>The element, or null when the device is not connected.</returns>
    public Element? CreateInputElement(string? deviceId)
    {
        if (deviceId == null)
            return ElementFactory.Make("autoaudiosrc", null);
        
        EnsureStarted();
        
        var monitor = _monitor;
        var device = monitor?.GetDevices().FirstOrDefault(d => IsInputDevice(d) && GetStableId(d) == deviceId);
        
        if (device == null)
            return null;

        var element = device.CreateElement(null);
        
        using (var output = element?.GetStaticPad("src"))
        {
            if (output != null)
                return element;
        }

        // The PipeWire provider creates a pipewiresink for virtual sources ("Audio/Source/Virtual",
        // e.g. the Easy Effects output), so the source is created by hand for them
        element?.Dispose();
        string? nodeName;

        using (var deviceProperties = device.GetProperties())
            nodeName = deviceProperties?.GetString("node.name");
        
        if (nodeName == null)
            return null;

        var source = ElementFactory.Make("pipewiresrc", null);
        
        source?.SetProperty("target-object", nodeName);
        
        return source;
    }

    private static bool IsInputDevice(Device device)
    {
        // PulseAudio reports the monitors of output devices as sources too
        using var properties = device.GetProperties();

        if (properties == null)
            return true;

        // And WASAPI lists every output device once more as a source that records what it plays
        return properties.GetString("device.class") != "monitor" && !IsSet(properties, WasapiLoopbackProperty);
    }

    private static bool IsSet(Structure properties, string field) =>
        properties.HasField(field) && properties.GetBoolean(field, out var value) && value;

    private static IReadOnlyList<AudioDeviceInfo> ReadDevices(DeviceMonitor monitor, ILogger logger)
    {
        var result = new List<AudioDeviceInfo>();

        foreach (var device in monitor.GetDevices())
        {
            using var properties = device.GetProperties();
            
            if (!IsInputDevice(device))
                continue;

            var id = GetStableId(device);
            
            if (result.Any(d => d.Id == id))
                continue;

            // PipeWire and PulseAudio name it one way, WASAPI the other
            var isDefault = properties != null
                            && (IsSet(properties, "is-default") || IsSet(properties, "device.default"));
            
            // Serializing the properties is not free
            if (logger.IsEnabled(LogLevel.Trace))
                LogInputDevice(logger, device.DisplayName, device.DeviceClass, id, properties?.ToString() ?? "none");
            
            result.Add(new AudioDeviceInfo(id, device.DisplayName, isDefault));
        }

        return result;
    }

    private static string GetStableId(Device device)
    {
        using var properties = device.GetProperties();

        if (properties != null)
        {
            foreach (var property in StableIdProperties)
            {
                if (!properties.HasField(property))
                    continue;

                var value = properties.GetString(property);
                
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        return device.DisplayName;
    }

    public void Dispose()
    {
        lock (_startLock)
        {
            var monitor = _monitor;
            
            if (monitor == null)
                return;
            
            _monitor = null;
            _busWatcher?.Dispose();
            _busWatcher = null;
            monitor.Stop();
            monitor.Dispose();
        }
    }
}
