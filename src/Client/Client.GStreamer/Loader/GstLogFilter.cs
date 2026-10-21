using Client.GStreamer.Interop;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Loader;

/// <summary>
/// Drops a few known and harmless GLib criticals that the PipeWire plugin of GStreamer emits
/// whenever it converts the formats of a video node, e.g. a camera with an infrared GRAY8
/// sensor. The device monitor makes it do that for every node, even when only audio devices
/// are requested. Every other message still reaches the default handler.
/// </summary>
internal static unsafe partial class GstLogFilter
{
    // G_LOG_LEVEL_CRITICAL
    private const int CriticalLevel = 1 << 3;

    private static readonly string[] Domains = ["GStreamer", "GStreamer-Video"];

    private static readonly string[] SuppressedMessages =
    [
        // SPA ranges with min == max turned into a GstIntRange
        "gst_value_collect_int_range: assertion",
        "range start is not smaller than end for `GstIntRange'",
        // A DMA-BUF format without a DRM fourcc, e.g. GRAY8
        "gst_video_dma_drm_fourcc_to_string: assertion 'fourcc != DRM_FORMAT_INVALID'"
    ];

    private static ILogger? _logger;
    private static int _isInstalled;
    private static int _suppressedCount;

    [DllImport(NativeLibraries.GLib)]
    private static extern uint g_log_set_handler([MarshalAs(UnmanagedType.LPUTF8Str)] string domain, int levels,
        delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, void> handler, IntPtr data);

    [DllImport(NativeLibraries.GLib)]
    private static extern void g_log_default_handler(IntPtr domain, int level, IntPtr message, IntPtr data);

    public static void Install(ILogger logger)
    {
        if (Interlocked.Exchange(ref _isInstalled, 1) == 1)
            return;

        _logger = logger;

        foreach (var domain in Domains)
        {
            g_log_set_handler(domain, CriticalLevel, &Handle, IntPtr.Zero);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Handle(IntPtr domain, int level, IntPtr message, IntPtr data)
    {
        try
        {
            var text = Marshal.PtrToStringUTF8(message);

            if (text != null && SuppressedMessages.Any(text.StartsWith))
            {
                // Report the first one, so that the filter is not a complete mystery when debugging
                if (Interlocked.Increment(ref _suppressedCount) == 1 && _logger != null)
                    LogSuppressing(_logger, text);

                return;
            }
        }
        catch
        {
            // Nothing may be thrown into GLib; the message is then printed like any other
        }

        g_log_default_handler(domain, level, message, data);
    }
}
