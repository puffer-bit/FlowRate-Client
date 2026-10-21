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
        OperatingSystem.IsAndroid() ? "Android"
        : OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : Environment.OSVersion.Platform.ToString();

    /// <summary>
    /// Key of the DrawingImage resource of the system in Client.Avalonia.Assets/IconImages.axaml,
    /// or null: there are no icons for macOS and other platforms yet.
    /// </summary>
    public static string? IconKey { get; } =
        OperatingSystem.IsAndroid() ? "AndroidIcon"
        : OperatingSystem.IsWindows() ? "WindowsIcon"
        : OperatingSystem.IsLinux() ? "LinuxIcon"
        : null;
}
