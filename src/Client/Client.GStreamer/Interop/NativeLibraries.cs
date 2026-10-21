namespace Client.GStreamer.Interop;

/// <summary>
/// The native libraries the project calls directly, by their Windows names: the resolver in
/// Loader/GstNativeLibraryResolver.cs maps them to the files of the running system.
/// </summary>
internal static class NativeLibraries
{
    public const string GLib = "glib-2.0-0.dll";
    public const string GObject = "gobject-2.0-0.dll";
    public const string GStreamer = "gstreamer-1.0-0.dll";
    public const string GstVideo = "gstvideo-1.0-0.dll";
    public const string GstAllocators = "gstallocators-1.0-0.dll";
    public const string GstCuda = "gstcuda-1.0-0.dll";
    public const string GstD3D11 = "gstd3d11-1.0-0.dll";
}
