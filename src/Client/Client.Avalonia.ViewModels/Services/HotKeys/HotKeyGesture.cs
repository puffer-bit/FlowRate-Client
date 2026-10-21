using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Client.Avalonia.Controls.Menus;

namespace Client.Services.HotKeys;

/// <summary>
/// Modifiers and one physical key: the place of the key on the keyboard, not what it types, so a
/// hot key stays the same in every keyboard layout. Saved as "Ctrl+Shift+M" with the names of
/// <see cref="PhysicalKey"/>.
/// </summary>
public readonly record struct HotKeyGesture(KeyModifiers Modifiers, PhysicalKey Key)
{
    private const KeyModifiers AllModifiers =
        KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;

    private static readonly (KeyModifiers Modifier, string Name)[] ModifierNames =
    [
        (KeyModifiers.Control, "Ctrl"),
        (KeyModifiers.Shift, "Shift"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Meta, "Meta")
    ];

    /// <summary>
    /// The key types a character when pressed alone, so it toggles the action while the user types.
    /// </summary>
    public bool IsTyping => (Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0
                            && Key is (>= PhysicalKey.A and <= PhysicalKey.Z)
                                or (>= PhysicalKey.Digit0 and <= PhysicalKey.Digit9)
                                or (>= PhysicalKey.NumPad0 and <= PhysicalKey.NumPad9)
                                or PhysicalKey.Backquote or PhysicalKey.Backslash or PhysicalKey.BracketLeft
                                or PhysicalKey.BracketRight or PhysicalKey.Comma or PhysicalKey.Equal
                                or PhysicalKey.Minus or PhysicalKey.Period or PhysicalKey.Quote
                                or PhysicalKey.Semicolon or PhysicalKey.Slash or PhysicalKey.Space
                                or PhysicalKey.IntlBackslash;

    public override string ToString()
    {
        var modifiers = Modifiers;
        return string.Join('+', ModifierNames
            .Where(modifier => modifiers.HasFlag(modifier.Modifier))
            .Select(modifier => modifier.Name)
            .Append(Key.ToString()));
    }

    public static bool TryParse(string? text, out HotKeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var modifiers = KeyModifiers.None;
        var parts = text.Split('+');
        foreach (var part in parts[..^1])
        {
            var index = Array.FindIndex(ModifierNames, modifier => modifier.Name == part);
            if (index < 0)
                return false;
            modifiers |= ModifierNames[index].Modifier;
        }

        if (!Enum.TryParse<PhysicalKey>(parts[^1], out var key) || key == PhysicalKey.None || IsModifier(key))
            return false;

        gesture = new HotKeyGesture(modifiers & AllModifiers, key);
        return true;
    }

    /// <summary>The modifier a key is, or none for any other key.</summary>
    public static KeyModifiers ModifierOf(PhysicalKey key) => key switch
    {
        PhysicalKey.ControlLeft or PhysicalKey.ControlRight => KeyModifiers.Control,
        PhysicalKey.ShiftLeft or PhysicalKey.ShiftRight => KeyModifiers.Shift,
        PhysicalKey.AltLeft or PhysicalKey.AltRight => KeyModifiers.Alt,
        PhysicalKey.MetaLeft or PhysicalKey.MetaRight => KeyModifiers.Meta,
        _ => KeyModifiers.None
    };

    public static bool IsModifier(PhysicalKey key) => ModifierOf(key) != KeyModifiers.None;

    /// <summary>The keycaps to show, modifiers first.</summary>
    public IReadOnlyList<GestureKey> ToKeycaps()
    {
        var labels = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Control))
            labels.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Shift))
            labels.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Alt))
            labels.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Meta))
            labels.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        labels.Add(KeyLabel(Key));
        return labels.Select((label, i) => new GestureKey(label, i == 0)).ToList();
    }

    private static string KeyLabel(PhysicalKey key) => key switch
    {
        >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9 => ((char)('0' + (key - PhysicalKey.Digit0))).ToString(),
        >= PhysicalKey.NumPad0 and <= PhysicalKey.NumPad9 => "Num " + (char)('0' + (key - PhysicalKey.NumPad0)),
        PhysicalKey.NumPadAdd => "Num +",
        PhysicalKey.NumPadSubtract => "Num -",
        PhysicalKey.NumPadMultiply => "Num *",
        PhysicalKey.NumPadDivide => "Num /",
        PhysicalKey.NumPadDecimal => "Num .",
        PhysicalKey.NumPadEnter => "Num Enter",
        PhysicalKey.Backquote => "`",
        PhysicalKey.Backslash or PhysicalKey.IntlBackslash => "\\",
        PhysicalKey.BracketLeft => "[",
        PhysicalKey.BracketRight => "]",
        PhysicalKey.Comma => ",",
        PhysicalKey.Equal => "=",
        PhysicalKey.Minus => "-",
        PhysicalKey.Period => ".",
        PhysicalKey.Quote => "'",
        PhysicalKey.Semicolon => ";",
        PhysicalKey.Slash => "/",
        PhysicalKey.Delete => "Del",
        PhysicalKey.Escape => "Esc",
        PhysicalKey.PageUp => "PgUp",
        PhysicalKey.PageDown => "PgDn",
        PhysicalKey.ArrowUp => "↑",
        PhysicalKey.ArrowDown => "↓",
        PhysicalKey.ArrowLeft => "←",
        PhysicalKey.ArrowRight => "→",
        PhysicalKey.PrintScreen => "PrtSc",
        PhysicalKey.ScrollLock => "ScrLk",
        PhysicalKey.ContextMenu => "Menu",
        _ => key.ToString()
    };
}
