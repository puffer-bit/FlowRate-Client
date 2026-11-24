using System.Runtime.InteropServices;

namespace Client.Platform;

/// <summary>
/// The operating system the client runs on, as the server shows it in the user's list of sessions and to other
/// users, and the journal names it.
/// </summary>
public static class DevicePlatform
{
    public static string Name =>
        OperatingSystem.IsBrowser() ? "Browser"
        : OperatingSystem.IsAndroid() ? "Android"
        : OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : "Unknown";

    /// <summary>
    /// The system and its version, shorter than <see cref="RuntimeInformation.OSDescription"/>: "Windows 11 (26100)",
    /// "Ubuntu 26.04 LTS", "macOS 26.1". Android names its release better itself, and its host tells it instead.
    /// </summary>
    public static string OsVersion { get; } = DescribeSystem();

    /// <summary>The processor architecture of the system: "x64", "arm64"...</summary>
    public static string Architecture { get; } = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    private static string DescribeSystem()
    {
        var version = Environment.OSVersion.Version;
        try
        {
            if (OperatingSystem.IsWindows())
                // Windows 11 still says 10.0; its builds start at 22000
                return $"Windows {(version.Build >= 22000 ? "11" : "10")} ({version.Build})";
            if (OperatingSystem.IsMacOS())
                return $"macOS {version.Major}.{version.Minor}";
            if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid() && LinuxDistribution() is { } distribution)
                return distribution;
        }
        catch (Exception)
        {
            // What the runtime says will do
        }

        return RuntimeInformation.OSDescription;
    }

    /// <summary>PRETTY_NAME of os-release: "Ubuntu 26.04 LTS", "Arch Linux"; null when there is none.</summary>
    private static string? LinuxDistribution()
    {
        foreach (var path in new[] { "/etc/os-release", "/usr/lib/os-release" })
        {
            if (!File.Exists(path))
                continue;

            foreach (var line in File.ReadLines(path))
            {
                if (!line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    continue;

                var name = line["PRETTY_NAME=".Length..].Trim().Trim('"', '\'');
                return name.Length > 0 ? name : null;
            }
        }

        return null;
    }
}
