using System;

namespace Client.Services;

/// <summary>
/// Runtime appearance choices. The custom title bar is independent of experimental features.
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
        if (IsEnabled == isEnabled && UseCustomTitleBar == useCustomTitleBar)
            return;

        IsEnabled = isEnabled;
        UseCustomTitleBar = useCustomTitleBar;
        Changed?.Invoke();
    }
}
