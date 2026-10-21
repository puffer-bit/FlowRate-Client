using System.Runtime.InteropServices;
using Gst.Interop;

namespace Client.GStreamer.Interop;

/// <summary>
/// Object properties read and written with exact reference counting.
/// </summary>
/// <remarks>
/// A leaked reference to an object of webrtcbin keeps its ICE agent, its sockets and its threads
/// after the session is left, so these reads go to GObject directly, where a getter hands over
/// exactly one reference, and the caller decides whether a wrapper is made for it at all.
/// </remarks>
internal static class GstNative
{
    [DllImport(NativeLibraries.GObject)]
    private static extern void g_object_get(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string property, out IntPtr value, IntPtr terminator);

    [DllImport(NativeLibraries.GObject)]
    private static extern void g_object_get(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string property, out int value, IntPtr terminator);

    [DllImport(NativeLibraries.GObject)]
    private static extern void g_object_set(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string property, int value, IntPtr terminator);

    [DllImport(NativeLibraries.GObject)]
    private static extern void g_object_unref(IntPtr instance);

    [DllImport(NativeLibraries.GObject)]
    private static extern void g_signal_emit_by_name(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, int argument, out IntPtr result);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_structure_get_value(IntPtr structure,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fieldName);

    [DllImport(NativeLibraries.GObject)]
    private static extern IntPtr g_value_get_boxed(IntPtr value);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_mini_object_is_writable(IntPtr miniObject);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_buffer_map(IntPtr buffer, out Gst.MapInfo info, Gst.MapFlags flags);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_buffer_unmap(IntPtr buffer, ref Gst.MapInfo info);

    // G_TYPE_DOUBLE, and the size of a GValue: { GType g_type; 16 bytes of data }
    private const nint DoubleType = 15 << 2;
    private const int ValueSize = 24;

    // GstMessage on a 64-bit system: the mini object (64 bytes), the type with its padding (8) and
    // the timestamp (8) come before the source
    private const int MessageSourceOffset = 80;

    /// <summary>
    /// A reference to the object in <paramref name="property"/>, to be given back with <see cref="Release"/>.
    /// </summary>
    /// <returns>Zero when the property holds no object.</returns>
    public static IntPtr GetObject(IntPtr instance, string property)
    {
        g_object_get(instance, property, out IntPtr value, IntPtr.Zero);

        return value;
    }

    /// <summary>
    /// The object in <paramref name="property"/> as a wrapper that owns one reference and gives it
    /// back when disposed.
    /// </summary>
    public static T? GetObject<T>(Gst.GObject.Object owner, string property) where T : Gst.GObject.Object
    {
        var handle = GetObject(owner.Handle, property);

        return handle == IntPtr.Zero ? null : Wrap<T>(handle);
    }

    /// <summary>
    /// Emits an action signal that takes an index and returns an object, such as get-transceiver of
    /// webrtcbin. The wrapper owns the returned reference and gives it back when disposed.
    /// </summary>
    public static T? EmitGetObject<T>(Gst.GObject.Object target, string signal, int index) where T : Gst.GObject.Object
    {
        g_signal_emit_by_name(target.Handle, signal, index, out var result);

        return result == IntPtr.Zero ? null : Wrap<T>(result);
    }

    // Takes over the reference in the handle, also when the object turns out to be of another type
    private static T? Wrap<T>(IntPtr handle) where T : Gst.GObject.Object
    {
        var wrapper = Gst.GObject.Object.FromNative(handle, Transfer.Full);

        if (wrapper is T typed)
            return typed;

        wrapper?.Dispose();
        return null;
    }

    /// <summary>
    /// An enumeration or integer property.
    /// </summary>
    public static int GetInt32(Gst.GObject.Object owner, string property)
    {
        g_object_get(owner.Handle, property, out int value, IntPtr.Zero);

        return value;
    }

    public static int GetInt32(IntPtr instance, string property)
    {
        g_object_get(instance, property, out int value, IntPtr.Zero);

        return value;
    }

    public static uint GetUInt32(IntPtr instance, string property)
    {
        g_object_get(instance, property, out int value, IntPtr.Zero);

        return (uint)value;
    }

    public static void SetBoolean(IntPtr instance, string property, bool value) =>
        g_object_set(instance, property, value ? 1 : 0, IntPtr.Zero);

    /// <summary>
    /// The object that posted a message, without a wrapper and without a reference.
    /// </summary>
    public static IntPtr GetMessageSource(Gst.Message message) =>
        Marshal.ReadIntPtr(message.Handle, MessageSourceOffset);

    /// <summary>
    /// Maps a buffer for writing in place, without a wrapper: a wrapper holds a reference of its own,
    /// and a buffer with two references is not writable.
    /// </summary>
    /// <returns>False when the buffer is shared, and so must not be changed.</returns>
    public static bool TryMapWritable(IntPtr buffer, out Gst.MapInfo info)
    {
        info = default;

        return gst_mini_object_is_writable(buffer) != 0 &&
               gst_buffer_map(buffer, out info, Gst.MapFlags.Read | Gst.MapFlags.Write) != 0;
    }

    public static void Unmap(IntPtr buffer, ref Gst.MapInfo info) => gst_buffer_unmap(buffer, ref info);

    public static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
            g_object_unref(instance);
    }

    /// <summary>
    /// Reads the message of the GError in a promise reply. GStreamer cannot serialize a GError, so
    /// the message is read from the native struct
    /// <c>{ GQuark domain; gint code; gchar *message; }</c>.
    /// </summary>
    public static string? ReadErrorMessage(Gst.Structure reply)
    {
        try
        {
            var value = gst_structure_get_value(reply.Handle, "error");

            if (value == IntPtr.Zero)
                return null;

            var error = g_value_get_boxed(value);

            if (error == IntPtr.Zero)
                return null;

            return Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The largest number in a value that holds a GValueArray of doubles, or null when it holds none.
    /// </summary>
    /// <remarks>
    /// The binding has no wrapper for a GValueArray, so the array is read as the native struct
    /// <c>{ guint n_values; GValue *values; }</c>.
    /// </remarks>
    public static double? ReadLargestDouble(in Gst.GObject.Value arrayValue)
    {
        var array = arrayValue.GetBoxed();

        if (array == IntPtr.Zero)
            return null;

        var count = Marshal.ReadInt32(array);
        var values = Marshal.ReadIntPtr(array, IntPtr.Size);
        double? largest = null;

        for (var index = 0; index < count; index++)
        {
            var item = values + index * ValueSize;

            if (Marshal.ReadIntPtr(item) != DoubleType)
                continue;

            var number = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size));

            if (largest == null || number > largest)
                largest = number;
        }

        return largest;
    }
}
