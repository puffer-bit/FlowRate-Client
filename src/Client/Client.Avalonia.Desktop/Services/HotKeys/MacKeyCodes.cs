using Avalonia.Input;

namespace Client.Services.HotKeys;

/// <summary>
/// The virtual key codes of macOS (kVK_* of Carbon), which name a place on the keyboard as a
/// physical key does, whatever the layout types.
/// </summary>
internal static class MacKeyCodes
{
    // The device-dependent flags of CGEventFlags, one per modifier key
    private const ulong ControlLeftFlag = 0x1;
    private const ulong ShiftLeftFlag = 0x2;
    private const ulong ShiftRightFlag = 0x4;
    private const ulong MetaLeftFlag = 0x8;
    private const ulong MetaRightFlag = 0x10;
    private const ulong AltLeftFlag = 0x20;
    private const ulong AltRightFlag = 0x40;
    private const ulong ControlRightFlag = 0x2000;
    private const ulong CapsLockFlag = 0x10000;

    /// <summary>Whether the modifier <paramref name="key"/> is held by the flags of an event.</summary>
    public static bool IsHeld(PhysicalKey key, ulong flags) => (flags & FlagOf(key)) != 0;

    private static ulong FlagOf(PhysicalKey key) => key switch
    {
        PhysicalKey.ControlLeft => ControlLeftFlag,
        PhysicalKey.ControlRight => ControlRightFlag,
        PhysicalKey.ShiftLeft => ShiftLeftFlag,
        PhysicalKey.ShiftRight => ShiftRightFlag,
        PhysicalKey.MetaLeft => MetaLeftFlag,
        PhysicalKey.MetaRight => MetaRightFlag,
        PhysicalKey.AltLeft => AltLeftFlag,
        PhysicalKey.AltRight => AltRightFlag,
        PhysicalKey.CapsLock => CapsLockFlag,
        _ => 0
    };

    public static PhysicalKey ToPhysicalKey(int code) => code switch
    {
        0x00 => PhysicalKey.A,
        0x01 => PhysicalKey.S,
        0x02 => PhysicalKey.D,
        0x03 => PhysicalKey.F,
        0x04 => PhysicalKey.H,
        0x05 => PhysicalKey.G,
        0x06 => PhysicalKey.Z,
        0x07 => PhysicalKey.X,
        0x08 => PhysicalKey.C,
        0x09 => PhysicalKey.V,
        0x0A => PhysicalKey.IntlBackslash,
        0x0B => PhysicalKey.B,
        0x0C => PhysicalKey.Q,
        0x0D => PhysicalKey.W,
        0x0E => PhysicalKey.E,
        0x0F => PhysicalKey.R,
        0x10 => PhysicalKey.Y,
        0x11 => PhysicalKey.T,
        0x12 => PhysicalKey.Digit1,
        0x13 => PhysicalKey.Digit2,
        0x14 => PhysicalKey.Digit3,
        0x15 => PhysicalKey.Digit4,
        0x16 => PhysicalKey.Digit6,
        0x17 => PhysicalKey.Digit5,
        0x18 => PhysicalKey.Equal,
        0x19 => PhysicalKey.Digit9,
        0x1A => PhysicalKey.Digit7,
        0x1B => PhysicalKey.Minus,
        0x1C => PhysicalKey.Digit8,
        0x1D => PhysicalKey.Digit0,
        0x1E => PhysicalKey.BracketRight,
        0x1F => PhysicalKey.O,
        0x20 => PhysicalKey.U,
        0x21 => PhysicalKey.BracketLeft,
        0x22 => PhysicalKey.I,
        0x23 => PhysicalKey.P,
        0x24 => PhysicalKey.Enter,
        0x25 => PhysicalKey.L,
        0x26 => PhysicalKey.J,
        0x27 => PhysicalKey.Quote,
        0x28 => PhysicalKey.K,
        0x29 => PhysicalKey.Semicolon,
        0x2A => PhysicalKey.Backslash,
        0x2B => PhysicalKey.Comma,
        0x2C => PhysicalKey.Slash,
        0x2D => PhysicalKey.N,
        0x2E => PhysicalKey.M,
        0x2F => PhysicalKey.Period,
        0x30 => PhysicalKey.Tab,
        0x31 => PhysicalKey.Space,
        0x32 => PhysicalKey.Backquote,
        0x33 => PhysicalKey.Backspace,
        0x35 => PhysicalKey.Escape,
        0x36 => PhysicalKey.MetaRight,
        0x37 => PhysicalKey.MetaLeft,
        0x38 => PhysicalKey.ShiftLeft,
        0x39 => PhysicalKey.CapsLock,
        0x3A => PhysicalKey.AltLeft,
        0x3B => PhysicalKey.ControlLeft,
        0x3C => PhysicalKey.ShiftRight,
        0x3D => PhysicalKey.AltRight,
        0x3E => PhysicalKey.ControlRight,
        0x40 => PhysicalKey.F17,
        0x41 => PhysicalKey.NumPadDecimal,
        0x43 => PhysicalKey.NumPadMultiply,
        0x45 => PhysicalKey.NumPadAdd,
        0x47 => PhysicalKey.NumLock,
        0x48 => PhysicalKey.AudioVolumeUp,
        0x49 => PhysicalKey.AudioVolumeDown,
        0x4A => PhysicalKey.AudioVolumeMute,
        0x4B => PhysicalKey.NumPadDivide,
        0x4C => PhysicalKey.NumPadEnter,
        0x4E => PhysicalKey.NumPadSubtract,
        0x4F => PhysicalKey.F18,
        0x50 => PhysicalKey.F19,
        0x51 => PhysicalKey.NumPadEqual,
        0x52 => PhysicalKey.NumPad0,
        0x53 => PhysicalKey.NumPad1,
        0x54 => PhysicalKey.NumPad2,
        0x55 => PhysicalKey.NumPad3,
        0x56 => PhysicalKey.NumPad4,
        0x57 => PhysicalKey.NumPad5,
        0x58 => PhysicalKey.NumPad6,
        0x59 => PhysicalKey.NumPad7,
        0x5A => PhysicalKey.F20,
        0x5B => PhysicalKey.NumPad8,
        0x5C => PhysicalKey.NumPad9,
        0x5D => PhysicalKey.IntlYen,
        0x5E => PhysicalKey.IntlRo,
        0x5F => PhysicalKey.NumPadComma,
        0x60 => PhysicalKey.F5,
        0x61 => PhysicalKey.F6,
        0x62 => PhysicalKey.F7,
        0x63 => PhysicalKey.F3,
        0x64 => PhysicalKey.F8,
        0x65 => PhysicalKey.F9,
        0x67 => PhysicalKey.F11,
        0x69 => PhysicalKey.F13,
        0x6A => PhysicalKey.F16,
        0x6B => PhysicalKey.F14,
        0x6D => PhysicalKey.F10,
        0x6E => PhysicalKey.ContextMenu,
        0x6F => PhysicalKey.F12,
        0x71 => PhysicalKey.F15,
        0x72 => PhysicalKey.Insert,
        0x73 => PhysicalKey.Home,
        0x74 => PhysicalKey.PageUp,
        0x75 => PhysicalKey.Delete,
        0x76 => PhysicalKey.F4,
        0x77 => PhysicalKey.End,
        0x78 => PhysicalKey.F2,
        0x79 => PhysicalKey.PageDown,
        0x7A => PhysicalKey.F1,
        0x7B => PhysicalKey.ArrowLeft,
        0x7C => PhysicalKey.ArrowRight,
        0x7D => PhysicalKey.ArrowDown,
        0x7E => PhysicalKey.ArrowUp,
        _ => PhysicalKey.None
    };
}
