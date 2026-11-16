using System;

namespace Client.Services;

/// <summary>
/// The experimental features in effect. A feature works only while the experimental features as a
/// whole are enabled; its own choice is kept meanwhile.
/// </summary>
public static class ExperimentalFeatures
{
    public static bool IsEnabled { get; private set; }

    /// <summary>
    /// Whether the main window draws its own title bar instead of the one of the system. A platform
    /// with windows applies it; see <see cref="Changed"/>.
    /// </summary>
    public static bool UseCustomTitleBar { get; private set; }

    public static event Action? Changed;

    public static void Apply(bool isEnabled, bool useCustomTitleBar)
    {
        useCustomTitleBar = isEnabled && useCustomTitleBar;

        if (IsEnabled == isEnabled && UseCustomTitleBar == useCustomTitleBar)
            return;

        IsEnabled = isEnabled;
        UseCustomTitleBar = useCustomTitleBar;
        Changed?.Invoke();
    }
}
