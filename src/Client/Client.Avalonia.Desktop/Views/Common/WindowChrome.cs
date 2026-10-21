using System;

namespace Client.Views.Common;

/// <summary>
/// Whether the main window draws its own title bar, with the window buttons in the top bar, or
/// leaves the title bar to the system.
/// </summary>
internal static class WindowChrome
{
    public static bool IsCustom { get; private set; }

    public static event Action? Changed;

    public static void Apply(bool isCustom)
    {
        if (IsCustom == isCustom)
            return;

        IsCustom = isCustom;
        Changed?.Invoke();
    }
}
