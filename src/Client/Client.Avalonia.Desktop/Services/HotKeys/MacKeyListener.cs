using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input;
using Client.Core.Settings;

namespace Client.Services.HotKeys;

/// <summary>
/// A listening event tap of macOS on a thread of its own, with a run loop that delivers its events.
/// It only listens, so every key still reaches the application it is pressed in. The system lets it
/// hear other applications only under Input Monitoring in Privacy &amp; Security.
/// </summary>
internal sealed unsafe partial class MacKeyListener : IGlobalKeyListener
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int SessionTap = 1;
    private const int HeadInsert = 0;
    private const int ListenOnly = 1;

    private const uint KeyDown = 10;
    private const uint KeyUp = 11;
    private const uint FlagsChanged = 12;
    private const uint DisabledByTimeout = 0xFFFFFFFE;
    private const uint DisabledByUserInput = 0xFFFFFFFF;

    private const int AutorepeatField = 8;
    private const int KeycodeField = 9;

    // The tap callback is static: there is one listener at a time
    private static MacKeyListener? _current;

    private readonly GlobalKeyHandler _onKey;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly HashSet<PhysicalKey> _down = [];
    private IntPtr _tap;
    private IntPtr _runLoop;
    private volatile bool _stopping;

    public GlobalHotKeysBackend Kind => GlobalHotKeysBackend.MacEventTap;

    private MacKeyListener(GlobalKeyHandler onKey)
    {
        _onKey = onKey;
        _thread = new Thread(Run) { IsBackground = true, Name = "Hot keys (event tap)" };
    }

    /// <returns>Null while Input Monitoring is not allowed; the system is asked to allow it then.</returns>
    public static MacKeyListener? TryStart(GlobalKeyHandler onKey)
    {
        // Adds the application to the list in the settings and asks the user once
        if (!CGPreflightListenEventAccess())
        {
            CGRequestListenEventAccess();
            return null;
        }

        var listener = new MacKeyListener(onKey);
        _current = listener;
        listener._thread.Start();
        listener._started.Wait();

        if (listener._tap != IntPtr.Zero)
            return listener;

        _current = null;
        return null;
    }

    private void Run()
    {
        const ulong mask = (1UL << (int)KeyDown) | (1UL << (int)KeyUp) | (1UL << (int)FlagsChanged);
        _tap = CGEventTapCreate(SessionTap, HeadInsert, ListenOnly, mask, &TapCallback, IntPtr.Zero);
        if (_tap == IntPtr.Zero)
        {
            _started.Set();
            return;
        }

        var source = CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
        _runLoop = CFRunLoopGetCurrent();
        var mode = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFRunLoopCommonModes"));
        CFRunLoopAddSource(_runLoop, source, mode);
        _started.Set();

        // In slices: a stop that comes before the loop runs is not lost
        var defaultMode = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFRunLoopDefaultMode"));
        while (!_stopping)
            CFRunLoopRunInMode(defaultMode, 0.5, false);

        CFRunLoopRemoveSource(_runLoop, source, mode);
        CFMachPortInvalidate(_tap);
        CFRelease(source);
        CFRelease(_tap);
    }

    [UnmanagedCallersOnly]
    private static IntPtr TapCallback(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo)
    {
        if (_current is not { } listener)
            return cgEvent;

        // The system turns a slow tap off; it is turned on again
        if (type is DisabledByTimeout or DisabledByUserInput)
            CGEventTapEnable(listener._tap, true);
        else
            listener.OnEvent(type, cgEvent);

        return cgEvent;
    }

    private void OnEvent(uint type, IntPtr cgEvent)
    {
        var keycode = (int)CGEventGetIntegerValueField(cgEvent, KeycodeField);
        var key = MacKeyCodes.ToPhysicalKey(keycode);
        if (key == PhysicalKey.None)
            return;

        if (type == FlagsChanged)
        {
            // A modifier tells whether it went down or up only by the flags it leaves
            var isDown = MacKeyCodes.IsHeld(key, CGEventGetFlags(cgEvent));
            if (isDown == _down.Contains(key))
                return;

            if (isDown)
                _down.Add(key);
            else
                _down.Remove(key);
            _onKey(key, isDown, false);
            return;
        }

        var isRepeat = type == KeyDown && CGEventGetIntegerValueField(cgEvent, AutorepeatField) != 0;
        _onKey(key, type == KeyDown, isRepeat);
    }

    public void Dispose()
    {
        _stopping = true;
        if (_runLoop != IntPtr.Zero)
            CFRunLoopStop(_runLoop);
        _thread.Join();
        _current = null;
        _started.Dispose();
    }

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGPreflightListenEventAccess();

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGRequestListenEventAccess();

    [LibraryImport(CoreGraphics)]
    private static partial IntPtr CGEventTapCreate(int tap, int place, int options, ulong eventsOfInterest,
        delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> callback, IntPtr userInfo);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.U1)] bool enable);

    [LibraryImport(CoreGraphics)]
    private static partial long CGEventGetIntegerValueField(IntPtr cgEvent, int field);

    [LibraryImport(CoreGraphics)]
    private static partial ulong CGEventGetFlags(IntPtr cgEvent);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);

    [LibraryImport(CoreFoundation)]
    private static partial void CFMachPortInvalidate(IntPtr port);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFRunLoopGetCurrent();

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopRemoveSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [LibraryImport(CoreFoundation)]
    private static partial int CFRunLoopRunInMode(IntPtr mode, double seconds,
        [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRunLoopStop(IntPtr runLoop);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr instance);
}
