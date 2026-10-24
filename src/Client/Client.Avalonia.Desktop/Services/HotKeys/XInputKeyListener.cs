using System;
using System.Runtime.InteropServices;
using System.Threading;
using Client.Core.Settings;

namespace Client.Services.HotKeys;

/// <summary>
/// Hears the raw key events of the X server (XInput 2), which go to every client that asks and are
/// never taken from the focused window. Under Wayland the X server is XWayland: it gets the keys
/// typed in Wayland windows only when the desktop lets it, on KDE by the setting of legacy X11 app
/// support. A connection of its own, used only by the thread of the listener.
/// </summary>
internal sealed partial class XInputKeyListener : IGlobalKeyListener
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXi = "libXi.so.6";

    private const int Success = 0;
    private const int GenericEvent = 35;
    private const int AllMasterDevices = 1;
    private const int RawKeyPress = 13;
    private const int RawKeyRelease = 14;
    private const int KeyRepeatFlag = 1 << 16;
    private const int PollMilliseconds = 500;

    // An X server key code is the key code of the Linux input layer plus this
    private const int KeyCodeOffset = 8;

    // XEvent is a union of 24 longs
    private const int EventSize = 24 * 8;

    // A bit for each of the 256 key codes
    private const int KeymapSize = 32;

    // Offsets in XGenericEventCookie and XIRawEvent on a 64-bit machine
    private const int CookieExtensionOffset = 32;
    private const int CookieEventTypeOffset = 36;
    private const int CookieDataOffset = 48;
    private const int RawDetailOffset = 56;
    private const int RawFlagsOffset = 60;

    private readonly GlobalKeyHandler _onKey;
    private readonly GlobalModifierSync _onModifierSync;
    private readonly IntPtr _display;
    private readonly int _opcode;
    private readonly Thread _thread;
    private volatile bool _isStopping;

    public GlobalHotKeysBackend Kind => GlobalHotKeysBackend.XInput;

    private XInputKeyListener(GlobalKeyHandler onKey, GlobalModifierSync onModifierSync, IntPtr display, int opcode)
    {
        _onKey = onKey;
        _onModifierSync = onModifierSync;
        _display = display;
        _opcode = opcode;
        _thread = new Thread(Run) { IsBackground = true, Name = "Hot keys (XInput)" };
    }

    /// <summary>Null without an X server or its XInput 2 extension.</summary>
    public static unsafe XInputKeyListener? TryStart(GlobalKeyHandler onKey, GlobalModifierSync onModifierSync)
    {
        if (IntPtr.Size != 8 || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return null;

        IntPtr display;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return null;
        }

        if (display == IntPtr.Zero)
            return null;

        try
        {
            if (!XQueryExtension(display, "XInputExtension", out var opcode, out _, out _))
            {
                XCloseDisplay(display);
                return null;
            }

            // Before 2.1 the server holds raw events back while some client grabs the keyboard
            int major = 2, minor = 2;
            if (XIQueryVersion(display, ref major, ref minor) != Success)
            {
                XCloseDisplay(display);
                return null;
            }

            var mask = stackalloc byte[4];
            mask[RawKeyPress / 8] |= 1 << (RawKeyPress % 8);
            mask[RawKeyRelease / 8] |= 1 << (RawKeyRelease % 8);
            var eventMask = new XIEventMask { DeviceId = AllMasterDevices, MaskLength = 4, Mask = mask };
            XISelectEvents(display, XDefaultRootWindow(display), &eventMask, 1);
            XFlush(display);

            var listener = new XInputKeyListener(onKey, onModifierSync, display, opcode);
            listener._thread.Start();
            return listener;
        }
        catch (DllNotFoundException)
        {
            XCloseDisplay(display);
            return null;
        }
    }

    private unsafe void Run()
    {
        var connection = XConnectionNumber(_display);
        var xEvent = stackalloc byte[EventSize];
        var keymap = stackalloc byte[KeymapSize];

        try
        {
            while (!_isStopping)
            {
                if (XPending(_display) == 0)
                {
                    var pollFd = new Libc.PollFd { Fd = connection, Events = Libc.PollIn };
                    Libc.Poll(&pollFd, 1, PollMilliseconds);
                    continue;
                }

                XNextEvent(_display, xEvent);
                if (*(int*)xEvent != GenericEvent || *(int*)(xEvent + CookieExtensionOffset) != _opcode)
                    continue;

                if (!XGetEventData(_display, xEvent))
                    continue;

                try
                {
                    var type = *(int*)(xEvent + CookieEventTypeOffset);
                    if (type is RawKeyPress or RawKeyRelease)
                    {
                        var raw = *(byte**)(xEvent + CookieDataOffset);
                        var keyCode = *(int*)(raw + RawDetailOffset);
                        var flags = *(int*)(raw + RawFlagsOffset);
                        var key = LinuxKeyCodes.ToPhysicalKey(keyCode - KeyCodeOffset);
                        var isRepeat = (flags & KeyRepeatFlag) != 0;
                        if (type == RawKeyPress && !isRepeat && !HotKeyGesture.IsModifier(key))
                            ReportModifiers(keymap);
                        _onKey(key, type == RawKeyPress, isRepeat);
                    }
                }
                finally
                {
                    XFreeEventData(_display, xEvent);
                }
            }
        }
        finally
        {
            XCloseDisplay(_display);
        }
    }

    /// <summary>
    /// Reports the modifiers as the server sees them now. XWayland lets go of the keys it holds when
    /// the focus leaves for a Wayland window, after Alt+Tab with Alt still down, but sends no raw
    /// release for them, and one modifier left down would stop every hot key from matching.
    /// </summary>
    private unsafe void ReportModifiers(byte* keymap)
    {
        XQueryKeymap(_display, keymap);
        for (var keyCode = KeyCodeOffset; keyCode < KeymapSize * 8; keyCode++)
        {
            var key = LinuxKeyCodes.ToPhysicalKey(keyCode - KeyCodeOffset);
            if (HotKeyGesture.IsModifier(key))
                _onModifierSync(key, (keymap[keyCode / 8] & (1 << (keyCode % 8))) != 0);
        }
    }

    public void Dispose()
    {
        _isStopping = true;
        _thread.Join();
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct XIEventMask
    {
        public int DeviceId;
        public int MaskLength;
        public byte* Mask;
    }

    [LibraryImport(LibX11)]
    private static partial IntPtr XOpenDisplay(IntPtr name);

    [LibraryImport(LibX11)]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibX11)]
    private static partial int XFlush(IntPtr display);

    [LibraryImport(LibX11)]
    private static partial int XPending(IntPtr display);

    [LibraryImport(LibX11)]
    private static partial int XConnectionNumber(IntPtr display);

    [LibraryImport(LibX11)]
    private static partial nint XDefaultRootWindow(IntPtr display);

    [LibraryImport(LibX11)]
    private static unsafe partial int XNextEvent(IntPtr display, byte* xEvent);

    [LibraryImport(LibX11)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool XGetEventData(IntPtr display, byte* cookie);

    [LibraryImport(LibX11)]
    private static unsafe partial void XFreeEventData(IntPtr display, byte* cookie);

    [LibraryImport(LibX11)]
    private static unsafe partial int XQueryKeymap(IntPtr display, byte* keys);

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool XQueryExtension(IntPtr display, string name, out int opcode, out int firstEvent,
        out int firstError);

    [LibraryImport(LibXi)]
    private static partial int XIQueryVersion(IntPtr display, ref int major, ref int minor);

    [LibraryImport(LibXi)]
    private static unsafe partial int XISelectEvents(IntPtr display, nint window, XIEventMask* masks, int count);
}
