using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Client.GStreamer.Video.Mac;

/// <summary>
/// The functions of flowrate_sck.h, the C interface over ScreenCaptureKit next to the executable.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class ScreenCaptureKitNative
{
    private const string Library = "flowrate_sck";

    public const int Version = 1;

    public const int KindDisplay = 0;
    public const int KindWindow = 1;
    public const int KindAudio = 2;

    public const int ErrorAccessDenied = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct Callbacks
    {
        public delegate* unmanaged<nint, byte*, int, int, void> Video;
        public delegate* unmanaged<nint, float*, int, int, void> Audio;
        public delegate* unmanaged<nint, int, byte*, void> Stopped;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Config
    {
        public int Kind;
        public ulong Id;
        public int Framerate;
        public int MaxHeight;
        public int ShowCursor;
        public uint AudioPid;
        public int AudioExclude;
    }

    [LibraryImport(Library, EntryPoint = "fr_sck_version")]
    public static partial int GetVersion();

    [LibraryImport(Library, EntryPoint = "fr_sck_has_access")]
    public static partial int HasAccess();

    [LibraryImport(Library, EntryPoint = "fr_sck_request_access")]
    public static partial int RequestAccess();

    [LibraryImport(Library, EntryPoint = "fr_sck_list")]
    public static partial int List(int windows,
        delegate* unmanaged<nint, ulong, uint, int, int, byte*, void> callback, nint ctx);

    [LibraryImport(Library, EntryPoint = "fr_sck_start")]
    public static partial nint Start(Config* config, Callbacks* callbacks, nint ctx, byte* error, int errorSize);

    [LibraryImport(Library, EntryPoint = "fr_sck_stop")]
    public static partial void Stop(nint capture);
}
