using System;

namespace Client.Services;

/// <summary>
/// The operating system the application runs on, as the interface names and draws it.
/// </summary>
public static class CurrentPlatform
{
    /// <summary>
    /// The name of the system. A proper name, the same in every language.
    /// </summary>
    public static string Name { get; } =
        OperatingSystem.IsBrowser() ? "Browser"
        : OperatingSystem.IsAndroid() ? "Android"
        : OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : OperatingSystem.IsIOS() ? "iOS"
        : Environment.OSVersion.Platform.ToString();

    /// <summary>
    /// Key of the DrawingImage resource of the system in Client.Avalonia.Assets/IconImages.axaml,
    /// or null for a platform without one.
    /// </summary>
    public static string? IconKey { get; } =
        OperatingSystem.IsBrowser() ? "BrowserIcon"
        : OperatingSystem.IsAndroid() ? "AndroidIcon"
        : OperatingSystem.IsWindows() ? "WindowsIcon"
        : OperatingSystem.IsLinux() ? "LinuxIcon"
        : OperatingSystem.IsMacOS() ? "MacOSIcon"
        : OperatingSystem.IsIOS() ? "IPhoneIcon"
        : null;
}
