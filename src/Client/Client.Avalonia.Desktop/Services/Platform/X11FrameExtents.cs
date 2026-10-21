using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Client.Services.Platform;

/// <summary>
/// Tells an X11 window manager how much of a window around its visible frame is only shadow, so
/// that it places, snaps and tiles the window by the frame. Without it the shadow of a window that
/// draws its own frame counts as the window: a tiled window keeps a gap to the edge of the screen.
/// </summary>
internal static partial class X11FrameExtents
{
    private const string LibX11 = "libX11.so.6";
    private const nint CardinalType = 6;
    private const int ReplaceMode = 0;

    /// <param name="extents">Width of the part outside the frame on each side, in the units of the window.</param>
    public static void Set(Window window, Thickness extents)
    {
        if (!OperatingSystem.IsLinux() || window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle)
            return;

        // A connection of its own: the one of Avalonia is not exposed
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return;

        try
        {
            var scale = window.RenderScaling;
            // Left, right, top, bottom, in pixels. A 32-bit item of a property is a C long
            ReadOnlySpan<nint> values =
            [
                (nint)Math.Round(extents.Left * scale),
                (nint)Math.Round(extents.Right * scale),
                (nint)Math.Round(extents.Top * scale),
                (nint)Math.Round(extents.Bottom * scale)
            ];

            var property = XInternAtom(display, "_GTK_FRAME_EXTENTS", false);
            XChangeProperty(display, handle.Handle, property, CardinalType, 32, ReplaceMode,
                in MemoryMarshal.GetReference(values), values.Length);
            XFlush(display);
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    [LibraryImport(LibX11)]
    private static partial IntPtr XOpenDisplay(IntPtr name);

    [LibraryImport(LibX11)]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XInternAtom(IntPtr display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport(LibX11)]
    private static partial int XChangeProperty(IntPtr display, IntPtr window, nint property, nint type, int format,
        int mode, in nint data, int count);

    [LibraryImport(LibX11)]
    private static partial int XFlush(IntPtr display);
}
