using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Client.GStreamer.Video.Mac;

/// <summary>
/// A display or a window ScreenCaptureKit can capture.
/// </summary>
/// <param name="Id">CGDirectDisplayID of a display, CGWindowID of a window.</param>
/// <param name="ProcessId">The process that owns a window, 0 for a display.</param>
/// <param name="Width">Pixels of a display, points of a window.</param>
/// <param name="Title">The name of a display; the application and the title of a window.</param>
public sealed record ScreenCaptureKitSource(ulong Id, uint ProcessId, int Width, int Height, string Title);

/// <summary>
/// What can be captured on macOS, and whether the system allows it: Screen Recording in Privacy &amp;
/// Security covers the screens, the windows and the sound of the applications alike.
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe class ScreenCaptureKit
{
    private static readonly Lazy<bool> IsAvailableOnce = new(() =>
    {
        try
        {
            return ScreenCaptureKitNative.GetVersion() == ScreenCaptureKitNative.Version;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    });

    /// <summary>The library next to the executable is there and matches these bindings.</summary>
    public static bool IsAvailable => IsAvailableOnce.Value;

    public static bool HasAccess => IsAvailable && ScreenCaptureKitNative.HasAccess() != 0;

    /// <summary>
    /// Asks the system to allow Screen Recording. It shows its prompt only the first time; an answer
    /// takes effect after the application restarts.
    /// </summary>
    public static bool RequestAccess() => IsAvailable && ScreenCaptureKitNative.RequestAccess() != 0;

    /// <returns>Empty when the library is missing or the system refuses.</returns>
    public static IReadOnlyList<ScreenCaptureKitSource> GetDisplays() => List(windows: false);

    /// <returns>
    /// The windows of the other applications at the level of documents; empty when the library is
    /// missing or the system refuses.
    /// </returns>
    public static IReadOnlyList<ScreenCaptureKitSource> GetWindows() => List(windows: true);

    private static List<ScreenCaptureKitSource> List(bool windows)
    {
        var sources = new List<ScreenCaptureKitSource>();

        if (!IsAvailable)
            return sources;

        var handle = GCHandle.Alloc(sources);

        try
        {
            ScreenCaptureKitNative.List(windows ? 1 : 0, &OnSource, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return sources;
    }

    [UnmanagedCallersOnly]
    private static void OnSource(nint ctx, ulong id, uint processId, int width, int height, byte* title)
    {
        if (GCHandle.FromIntPtr(ctx).Target is List<ScreenCaptureKitSource> sources)
            sources.Add(new ScreenCaptureKitSource(id, processId, width, height,
                Marshal.PtrToStringUTF8((nint)title) ?? ""));
    }
}
