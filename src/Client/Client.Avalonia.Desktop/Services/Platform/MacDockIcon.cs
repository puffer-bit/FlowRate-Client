using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Client.Services.Platform;

/// <summary>
/// Gives the application its icon in the macOS Dock. A process started outside of an app bundle has
/// no Info.plist naming an icon, so the Dock shows the generic one of an executable. FlowRate.app
/// names its own.
/// </summary>
internal static partial class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const int Size = 512;

    // The artwork of a macOS app icon takes 824 of 1024 points, the rest is margin
    private const double ArtworkShare = 824.0 / 1024.0;

    private static readonly Uri Source = new("avares://Client.Avalonia.Assets/AppIcons/Headphones/png/app-icon-512.png");

    /// <summary>Sets the icon; call once the platform has created the application, on the UI thread.</summary>
    public static void Apply()
    {
        if (!OperatingSystem.IsMacOS() || AppContext.BaseDirectory.Contains(".app/Contents/MacOS/", StringComparison.Ordinal))
            return;

        var png = RenderWithMargin();
        var handle = GCHandle.Alloc(png, GCHandleType.Pinned);
        var data = IntPtr.Zero;
        var image = IntPtr.Zero;
        try
        {
            data = SendPtrLong(SendPtr(Class("NSData"), Selector("alloc")), Selector("initWithBytes:length:"),
                handle.AddrOfPinnedObject(), png.Length);
            image = SendPtr(SendPtr(Class("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
            if (image == IntPtr.Zero)
                return;

            var application = SendPtr(Class("NSApplication"), Selector("sharedApplication"));
            SendVoid(application, Selector("setApplicationIconImage:"), image);
        }
        finally
        {
            // NSApplication keeps its own reference to the image
            if (image != IntPtr.Zero)
                SendVoid(image, Selector("release"));
            if (data != IntPtr.Zero)
                SendVoid(data, Selector("release"));
            handle.Free();
        }
    }

    private static byte[] RenderWithMargin()
    {
        using var artwork = new Bitmap(AssetLoader.Open(Source));
        using var canvas = new RenderTargetBitmap(new PixelSize(Size, Size));
        var side = Size * ArtworkShare;
        var offset = (Size - side) / 2;
        using (var context = canvas.CreateDrawingContext())
            context.DrawImage(artwork, new Rect(offset, offset, side, side));

        using var stream = new MemoryStream();
        canvas.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendPtrLong(IntPtr receiver, IntPtr selector, IntPtr bytes, nint length);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(IntPtr receiver, IntPtr selector, IntPtr argument);
}
