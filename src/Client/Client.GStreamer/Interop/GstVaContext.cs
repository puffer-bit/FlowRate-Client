using System.Runtime.InteropServices;
using Gst;

namespace Client.GStreamer.Interop;

/// <summary>Shares one VA display across the capture bridge before any GPU frames are allocated.</summary>
internal static class GstVaContext
{
    private const string DisplayContext = "gst.va.display.handle";

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_query_new_context([MarshalAs(UnmanagedType.LPUTF8Str)] string contextType);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_pad_query(IntPtr pad, IntPtr query);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_query_parse_context(IntPtr query, out IntPtr context);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_element_set_context(IntPtr element, IntPtr context);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_mini_object_unref(IntPtr instance);

    public static bool Prepare(Pipeline pipeline, Element encoder)
    {
        // appsink/appsrc breaks upstream context queries as well as caps negotiation.
        // Open the encoder once to obtain its display, then return to NULL before
        // applying the context to every child. No active filter/display is replaced.
        if (encoder.SetState(State.Ready) == StateChangeReturn.Failure)
        {
            encoder.SetState(State.Null);
            return false;
        }

        var query = gst_query_new_context(DisplayContext);
        var prepared = false;
        try
        {
            using var pad = encoder.GetStaticPad("src");
            if (pad == null || gst_pad_query(pad.Handle, query) == 0)
                return false;

            gst_query_parse_context(query, out var context);
            if (context == IntPtr.Zero)
                return false;

            // The query retains the display while the temporary encoder is closed.
            encoder.SetState(State.Null);
            gst_element_set_context(pipeline.Handle, context);
            prepared = true;
            return true;
        }
        finally
        {
            // Closing after successful propagation would discard the shared display.
            // Failed queries leave no context to apply and still need the READY cleanup.
            if (!prepared)
                encoder.SetState(State.Null);
            gst_mini_object_unref(query);
        }
    }
}
