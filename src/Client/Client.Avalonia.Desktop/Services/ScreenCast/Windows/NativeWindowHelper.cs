using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Client.Services.ScreenCast.Windows;

/// <summary>
/// Lists the windows and monitors that can be captured. The calls are source-generated and the
/// callbacks are plain function pointers, so nothing here needs marshalling stubs built at run time.
/// </summary>
internal static unsafe partial class NativeWindowHelper
{
    private const int MaxTitleLength = 256;
    private const int DeviceNameLength = 32;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr state);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static partial int GetWindowText(IntPtr window, char* text, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip,
        delegate* unmanaged<IntPtr, IntPtr, Rect*, IntPtr, int> callback, IntPtr state);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumChildWindows(IntPtr parent, delegate* unmanaged<IntPtr, IntPtr, int> callback,
        IntPtr state);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, MonitorInfo* info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    // MONITORINFOEXW
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        public fixed char Device[DeviceNameLength];
    }

    // Hosts the frames of the Store applications, whose content runs in a process of its own
    private const string FrameHostProcessName = "ApplicationFrameHost";

    /// <param name="ProcessId">The process that draws the window, and plays its sound.</param>
    public sealed record WindowData(string Title, IntPtr Handle, uint ProcessId);

    /// <param name="DeviceName">System name of the display, such as DISPLAY1.</param>
    public sealed record MonitorData(string DeviceName, IntPtr Handle, int Width, int Height);

    public static List<WindowData> GetOpenWindows()
    {
        var windows = new List<WindowData>();
        var state = GCHandle.Alloc(windows);

        try
        {
            EnumWindows(&OnWindow, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        return windows;
    }

    public static List<MonitorData> GetMonitors()
    {
        var monitors = new List<MonitorData>();
        var state = GCHandle.Alloc(monitors);

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &OnMonitor, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        return monitors;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(IntPtr window, IntPtr state)
    {
        if (!IsWindowVisible(window))
            return 1;

        var buffer = stackalloc char[MaxTitleLength];
        var length = GetWindowText(window, buffer, MaxTitleLength);

        if (length <= 0)
            return 1;

        var title = new string(buffer, 0, length);

        if (!string.IsNullOrWhiteSpace(title))
            ((List<WindowData>)GCHandle.FromIntPtr(state).Target!).Add(new WindowData(title, window, GetContentProcessId(window)));

        return 1;
    }

    /// <summary>
    /// The process behind a window. A Store application draws into a child window of the frame
    /// host, so the sound comes from the process of that child, not from the host.
    /// </summary>
    private static uint GetContentProcessId(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);

        if (!IsFrameHost(processId))
            return processId;

        var state = new uint[] { processId, 0 };
        var handle = GCHandle.Alloc(state);

        try
        {
            EnumChildWindows(window, &OnChildWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return state[1] != 0 ? state[1] : processId;
    }

    private static bool IsFrameHost(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);

            return process.ProcessName.Equals(FrameHostProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // The process is gone already
            return false;
        }
    }

    // The state holds the process of the host and receives the first other one among the children
    [UnmanagedCallersOnly]
    private static int OnChildWindow(IntPtr window, IntPtr state)
    {
        var processes = (uint[])GCHandle.FromIntPtr(state).Target!;
        GetWindowThreadProcessId(window, out var processId);

        if (processId == processes[0])
            return 1;

        processes[1] = processId;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnMonitor(IntPtr monitor, IntPtr deviceContext, Rect* area, IntPtr state)
    {
        var info = new MonitorInfo { Size = Unsafe.SizeOf<MonitorInfo>() };

        if (!GetMonitorInfo(monitor, &info))
            return 1;

        // \\.\DISPLAY1
        var device = new string(info.Device).TrimStart('\\', '.');

        ((List<MonitorData>)GCHandle.FromIntPtr(state).Target!).Add(new MonitorData(device, monitor,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top));

        return 1;
    }
}
