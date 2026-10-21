using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Client.Tools.HardwareIdentifier;

public partial class GpuInfo
{
    public string Name { get; set; } = "Unknown";
    public string Vendor { get; set; } = "Unknown";

    // Detection queries WMI on Windows, which takes a moment, so it runs once per application.
    // Only the application report logs it; the encoders are picked by VideoEncoderSelector instead.
    private static readonly Lazy<List<GpuInfo>> CachedGpuInfo = new(DetectGpuInfo);

    public static List<GpuInfo> GetGpuInfo() => CachedGpuInfo.Value;

    // Never empty, callers take the first entry
    private static List<GpuInfo> DetectGpuInfo()
    {
        var gpus = OperatingSystem.IsWindows() ? GetWindowsGpuInfo()
            : OperatingSystem.IsLinux() ? GetLinuxGpuInfo()
            : new List<GpuInfo>();

        if (gpus.Count == 0)
            gpus.Add(new GpuInfo { Name = "Unknown", Vendor = "Unknown" });

        return gpus;
    }

    [SupportedOSPlatform("windows")]
    private static List<GpuInfo> GetWindowsGpuInfo()
    {
        var gpus = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController");
            using var results = searcher.Get();

            foreach (var adapter in results)
            {
                using (adapter)
                {
                    var name = adapter["Name"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    gpus.Add(new GpuInfo { Name = name, Vendor = adapter["AdapterCompatibility"]?.ToString()?.Trim() ?? "Unknown" });
                }
            }
        }
        catch (Exception ex)
        {
            gpus.Add(new GpuInfo { Name = "Error", Vendor = ex.Message });
        }
        return gpus;
    }

    /// <summary>
    /// Reads the PCI ids of the DRM cards from sysfs. There is no product name there without the
    /// pci.ids database, so the name is made of the vendor, the ids and the kernel driver.
    /// </summary>
    private static List<GpuInfo> GetLinuxGpuInfo()
    {
        const string drmDirectory = "/sys/class/drm";
        var gpus = new List<GpuInfo>();

        try
        {
            // cardN are the devices, cardN-HDMI-A-1 and the like are their connectors
            var cards = Directory.EnumerateDirectories(drmDirectory)
                .Select(Path.GetFileName)
                .Where(name => name != null && DrmCardName().IsMatch(name))
                .OrderBy(name => int.Parse(name!.AsSpan(4)));

            foreach (var card in cards)
            {
                var device = Path.Combine(drmDirectory, card!, "device");
                var vendorId = ReadPciId(Path.Combine(device, "vendor"));
                if (vendorId == null)
                    continue;

                var deviceId = ReadPciId(Path.Combine(device, "device")) ?? "????";
                var driver = new DirectoryInfo(Path.Combine(device, "driver")).ResolveLinkTarget(true)?.Name ?? "no driver";

                var vendor = vendorId switch
                {
                    "10de" => "NVIDIA",
                    "1002" => "AMD",
                    "8086" => "Intel",
                    _ => "Unknown"
                };

                gpus.Add(new GpuInfo { Name = $"{vendor} GPU {vendorId}:{deviceId} ({driver})", Vendor = vendor });
            }
        }
        catch (Exception ex)
        {
            gpus.Add(new GpuInfo { Name = "Error", Vendor = ex.Message });
        }
        return gpus;
    }

    // The files hold values like "0x10de"
    private static string? ReadPciId(string path)
    {
        if (!File.Exists(path))
            return null;

        var value = File.ReadAllText(path).Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..].ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"^card\d+$")]
    private static partial Regex DrmCardName();
}
