using Avalonia.Input;

namespace Client.Services.HotKeys;

/// <summary>
/// The key codes of the Linux input layer (linux/input-event-codes.h). An X server key code is one
/// of these plus 8, and the codes up to 88 are the scan codes of a PC keyboard, which Windows reports.
/// </summary>
internal static class LinuxKeyCodes
{
    private static readonly PhysicalKey[] Keys = Build();

    public static PhysicalKey ToPhysicalKey(int code) =>
        code >= 0 && code < Keys.Length ? Keys[code] : PhysicalKey.None;

    private static PhysicalKey[] Build()
    {
        var keys = new PhysicalKey[256];

        void Set(int code, PhysicalKey key) => keys[code] = key;

        Set(1, PhysicalKey.Escape);
        for (var i = 0; i < 9; i++)
            Set(2 + i, PhysicalKey.Digit1 + i);
        Set(11, PhysicalKey.Digit0);
        Set(12, PhysicalKey.Minus);
        Set(13, PhysicalKey.Equal);
        Set(14, PhysicalKey.Backspace);
        Set(15, PhysicalKey.Tab);

        // Letters go by rows of the keyboard, not by the alphabet
        PhysicalKey[] top = [PhysicalKey.Q, PhysicalKey.W, PhysicalKey.E, PhysicalKey.R, PhysicalKey.T,
            PhysicalKey.Y, PhysicalKey.U, PhysicalKey.I, PhysicalKey.O, PhysicalKey.P];
        PhysicalKey[] middle = [PhysicalKey.A, PhysicalKey.S, PhysicalKey.D, PhysicalKey.F, PhysicalKey.G,
            PhysicalKey.H, PhysicalKey.J, PhysicalKey.K, PhysicalKey.L];
        PhysicalKey[] bottom = [PhysicalKey.Z, PhysicalKey.X, PhysicalKey.C, PhysicalKey.V, PhysicalKey.B,
            PhysicalKey.N, PhysicalKey.M];
        for (var i = 0; i < top.Length; i++)
            Set(16 + i, top[i]);
        for (var i = 0; i < middle.Length; i++)
            Set(30 + i, middle[i]);
        for (var i = 0; i < bottom.Length; i++)
            Set(44 + i, bottom[i]);

        Set(26, PhysicalKey.BracketLeft);
        Set(27, PhysicalKey.BracketRight);
        Set(28, PhysicalKey.Enter);
        Set(29, PhysicalKey.ControlLeft);
        Set(39, PhysicalKey.Semicolon);
        Set(40, PhysicalKey.Quote);
        Set(41, PhysicalKey.Backquote);
        Set(42, PhysicalKey.ShiftLeft);
        Set(43, PhysicalKey.Backslash);
        Set(51, PhysicalKey.Comma);
        Set(52, PhysicalKey.Period);
        Set(53, PhysicalKey.Slash);
        Set(54, PhysicalKey.ShiftRight);
        Set(55, PhysicalKey.NumPadMultiply);
        Set(56, PhysicalKey.AltLeft);
        Set(57, PhysicalKey.Space);
        Set(58, PhysicalKey.CapsLock);
        for (var i = 0; i < 10; i++)
            Set(59 + i, PhysicalKey.F1 + i);
        Set(69, PhysicalKey.NumLock);
        Set(70, PhysicalKey.ScrollLock);
        Set(71, PhysicalKey.NumPad7);
        Set(72, PhysicalKey.NumPad8);
        Set(73, PhysicalKey.NumPad9);
        Set(74, PhysicalKey.NumPadSubtract);
        Set(75, PhysicalKey.NumPad4);
        Set(76, PhysicalKey.NumPad5);
        Set(77, PhysicalKey.NumPad6);
        Set(78, PhysicalKey.NumPadAdd);
        Set(79, PhysicalKey.NumPad1);
        Set(80, PhysicalKey.NumPad2);
        Set(81, PhysicalKey.NumPad3);
        Set(82, PhysicalKey.NumPad0);
        Set(83, PhysicalKey.NumPadDecimal);
        Set(86, PhysicalKey.IntlBackslash);
        Set(87, PhysicalKey.F11);
        Set(88, PhysicalKey.F12);
        Set(89, PhysicalKey.IntlRo);
        Set(96, PhysicalKey.NumPadEnter);
        Set(97, PhysicalKey.ControlRight);
        Set(98, PhysicalKey.NumPadDivide);
        Set(99, PhysicalKey.PrintScreen);
        Set(100, PhysicalKey.AltRight);
        Set(102, PhysicalKey.Home);
        Set(103, PhysicalKey.ArrowUp);
        Set(104, PhysicalKey.PageUp);
        Set(105, PhysicalKey.ArrowLeft);
        Set(106, PhysicalKey.ArrowRight);
        Set(107, PhysicalKey.End);
        Set(108, PhysicalKey.ArrowDown);
        Set(109, PhysicalKey.PageDown);
        Set(110, PhysicalKey.Insert);
        Set(111, PhysicalKey.Delete);
        Set(113, PhysicalKey.AudioVolumeMute);
        Set(114, PhysicalKey.AudioVolumeDown);
        Set(115, PhysicalKey.AudioVolumeUp);
        Set(117, PhysicalKey.NumPadEqual);
        Set(119, PhysicalKey.Pause);
        Set(121, PhysicalKey.NumPadComma);
        Set(124, PhysicalKey.IntlYen);
        Set(125, PhysicalKey.MetaLeft);
        Set(126, PhysicalKey.MetaRight);
        Set(127, PhysicalKey.ContextMenu);
        Set(163, PhysicalKey.MediaTrackNext);
        Set(164, PhysicalKey.MediaPlayPause);
        Set(165, PhysicalKey.MediaTrackPrevious);
        Set(166, PhysicalKey.MediaStop);
        for (var i = 0; i < 12; i++)
            Set(183 + i, PhysicalKey.F13 + i);

        return keys;
    }
}
