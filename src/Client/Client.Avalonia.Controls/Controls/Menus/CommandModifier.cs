using Avalonia.Input;

namespace Client.Avalonia.Controls.Menus;

/// <summary>
/// The key of application shortcuts: Ctrl, and Cmd on macOS, where Ctrl is left to text editing.
/// Shortcuts are written with Ctrl ("Ctrl+B") and mean this key on every platform.
/// </summary>
public static class CommandModifier
{
    /// <summary>
    /// Modifiers of a key event as shortcuts are written: on macOS Cmd becomes Control and Control
    /// becomes Meta, so that "Ctrl+B" matches Cmd+B there and Ctrl+B matches nothing.
    /// </summary>
    public static KeyModifiers Normalize(KeyModifiers modifiers)
    {
        if (!OperatingSystem.IsMacOS())
            return modifiers;

        var swapped = modifiers & ~(KeyModifiers.Control | KeyModifiers.Meta);
        if (modifiers.HasFlag(KeyModifiers.Meta))
            swapped |= KeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Control))
            swapped |= KeyModifiers.Meta;
        return swapped;
    }
}
