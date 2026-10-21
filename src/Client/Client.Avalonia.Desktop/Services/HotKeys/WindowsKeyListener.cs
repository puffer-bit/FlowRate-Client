using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input;
using Client.Core.Settings;

namespace Client.Services.HotKeys;

/// <summary>
/// A low level keyboard hook of Windows on a thread of its own. Every key is passed on, so it still
/// reaches the application it is pressed in.
/// </summary>
internal sealed unsafe partial class WindowsKeyListener : IGlobalKeyListener
{
    private const int KeyboardLowLevelHook = 13;
    private const int KeyDownMessage = 0x100;
    private const int SystemKeyDownMessage = 0x104;
    private const int QuitMessage = 0x12;
    private const uint ExtendedFlag = 0x1;

    // The hook procedure is static: there is one listener at a time
    private static WindowsKeyListener? _current;

    private readonly GlobalKeyHandler _onKey;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly HashSet<PhysicalKey> _down = [];
    private uint _threadId;
    private IntPtr _hook;

    public GlobalHotKeysBackend Kind => GlobalHotKeysBackend.WindowsHook;

    private WindowsKeyListener(GlobalKeyHandler onKey)
    {
        _onKey = onKey;
        _thread = new Thread(Run) { IsBackground = true, Name = "Hot keys (hook)" };
    }

    public static WindowsKeyListener? TryStart(GlobalKeyHandler onKey)
    {
        var listener = new WindowsKeyListener(onKey);
        _current = listener;
        listener._thread.Start();
        listener._started.Wait();

        if (listener._hook != IntPtr.Zero)
            return listener;

        _current = null;
        return null;
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookExW(KeyboardLowLevelHook, &HookProcedure, GetModuleHandleW(null), 0);
        _started.Set();
        if (_hook == IntPtr.Zero)
            return;

        // The hook is called through the message loop of the thread that set it
        Message message;
        while (GetMessageW(&message, IntPtr.Zero, 0, 0) > 0)
        {
        }

        UnhookWindowsHookEx(_hook);
    }

    [UnmanagedCallersOnly]
    private static nint HookProcedure(int code, nint message, nint data)
    {
        if (code >= 0 && _current is { } listener)
        {
            var key = (KeyboardHookData*)data;
            listener.OnKey(key, message is KeyDownMessage or SystemKeyDownMessage);
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void OnKey(KeyboardHookData* data, bool isDown)
    {
        var key = ToPhysicalKey(data->VirtualKey, data->ScanCode, (data->Flags & ExtendedFlag) != 0);
        if (key == PhysicalKey.None)
            return;

        // Windows repeats a held key as more presses
        var isRepeat = isDown && !_down.Add(key);
        if (!isDown)
            _down.Remove(key);

        _onKey(key, isDown, isRepeat);
    }

    private static PhysicalKey ToPhysicalKey(uint virtualKey, uint scanCode, bool isExtended)
    {
        switch (virtualKey)
        {
            case 0x13: return PhysicalKey.Pause;
            case 0x90: return PhysicalKey.NumLock;
            case 0xAD: return PhysicalKey.AudioVolumeMute;
            case 0xAE: return PhysicalKey.AudioVolumeDown;
            case 0xAF: return PhysicalKey.AudioVolumeUp;
            case 0xB0: return PhysicalKey.MediaTrackNext;
            case 0xB1: return PhysicalKey.MediaTrackPrevious;
            case 0xB2: return PhysicalKey.MediaStop;
            case 0xB3: return PhysicalKey.MediaPlayPause;
            case >= 0x7C and <= 0x87: return PhysicalKey.F13 + (int)(virtualKey - 0x7C);
        }

        if (!isExtended)
            return LinuxKeyCodes.ToPhysicalKey((int)scanCode);

        // The keys that share a scan code with one of the number pad, told apart by the extended flag
        return scanCode switch
        {
            0x1C => PhysicalKey.NumPadEnter,
            0x1D => PhysicalKey.ControlRight,
            0x35 => PhysicalKey.NumPadDivide,
            0x37 => PhysicalKey.PrintScreen,
            0x38 => PhysicalKey.AltRight,
            0x47 => PhysicalKey.Home,
            0x48 => PhysicalKey.ArrowUp,
            0x49 => PhysicalKey.PageUp,
            0x4B => PhysicalKey.ArrowLeft,
            0x4D => PhysicalKey.ArrowRight,
            0x4F => PhysicalKey.End,
            0x50 => PhysicalKey.ArrowDown,
            0x51 => PhysicalKey.PageDown,
            0x52 => PhysicalKey.Insert,
            0x53 => PhysicalKey.Delete,
            0x5B => PhysicalKey.MetaLeft,
            0x5C => PhysicalKey.MetaRight,
            0x5D => PhysicalKey.ContextMenu,
            _ => PhysicalKey.None
        };
    }

    public void Dispose()
    {
        PostThreadMessageW(_threadId, QuitMessage, 0, 0);
        _thread.Join();
        _current = null;
        _started.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetWindowsHookExW(int hookId, delegate* unmanaged<int, nint, nint, nint> procedure,
        IntPtr module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(IntPtr hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Message* message, IntPtr window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? moduleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
