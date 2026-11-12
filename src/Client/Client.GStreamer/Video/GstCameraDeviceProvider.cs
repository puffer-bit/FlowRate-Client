using Client.Application.Ports;
using Client.Core.Media;
using Gst;

namespace Client.GStreamer.Video;

/// <summary>Uses the same device objects for discovery and capture on Windows and Linux.</summary>
public sealed class GstCameraDeviceProvider : ICameraDeviceProvider
{
    public IReadOnlyList<CameraDevice> GetDevices()
    {
        if (!MediaRuntime.IsEnabled)
            return [];
        using var monitor = DeviceMonitor.New();
        monitor.AddFilter("Video/Source", null);
        // A snapshot also supports providers which can probe devices but cannot watch hotplug.
        var result = new List<CameraDevice>();
        foreach (var device in monitor.GetDevices())
        {
            using (device)
            {
                var id = Id(device);
                if (result.All(item => item.Id != id))
                    result.Add(new CameraDevice(id, device.DisplayName));
            }
        }
        return result;
    }

    internal static Element? CreateSource(string id, out string? formats)
    {
        formats = null;
        using var monitor = DeviceMonitor.New();
        monitor.AddFilter("Video/Source", null);
        Element? source = null;
        foreach (var device in monitor.GetDevices())
            using (device)
                if (source == null && Id(device) == id)
                {
                    using var caps = device.GetCaps();
                    formats = caps?.ToString();
                    source = device.CreateElement(null);
                }
        return source;
    }

    private static string Id(Device device)
    {
        using var properties = device.GetProperties();
        foreach (var key in new[] { "udev.id", "device.path", "api.v4l2.path", "object.path", "node.name", "device.id" })
            if (properties?.GetString(key) is { Length: > 0 } value)
                return key + ":" + value;
        return device.DeviceClass + ":" + device.DisplayName;
    }
}
