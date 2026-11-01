using System.Diagnostics;
using System.Runtime.InteropServices;
using Client.Core.Media;
using Gst;

namespace Client.GStreamer.Interop;

/// <summary>
/// Measures an encoder or a decoder from outside: a probe on its sink pad notes when each buffer goes
/// in, by its timestamp, and one on its source pad finds the buffer of the same timestamp coming out.
/// Encoders of screens and decoders of WebRTC streams keep the order of the pictures, so the pair is
/// the time the element spent on one picture, queues inside it included.
/// </summary>
/// <remarks>
/// <para>
/// Most elements keep the timestamps too. The ones that do not, such as the VA-API encoders, which
/// count their own from 1000 hours, are matched in order instead: a picture that comes out goes with
/// the oldest one in. A picture the element drops would then pair every later one with the one before
/// it, so the pictures waiting beyond the fewest ever seen after a picture came out are taken for
/// dropped. An element whose delay grows is measured a little short by that.
/// </para>
/// <para>
/// The probes run for every picture on the streaming threads of the element, so they make no wrapper
/// and take a lock for only a few reads and writes. They live as long as the pads and hold the timer
/// weakly; a disposed or collected timer leaves them doing nothing.
/// </para>
/// </remarks>
public sealed unsafe class GstCodecTimer : IDisposable
{
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_element_get_static_pad(IntPtr element, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_pad_add_probe(IntPtr pad, int mask,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> callback, IntPtr data,
        delegate* unmanaged<IntPtr, void> destroy);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_pad_probe_info_get_buffer(IntPtr info);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_object_unref(IntPtr instance);

    private const int ProbeBuffer = 1 << 4;
    private const int ProbeOk = 1;

    // See GstEncodedNative
    private const int BufferPtsOffset = 72;
    private const long ClockTimeNone = -1;

    // Pictures in flight inside the element: an encoder holds one or two, a decoder a few more
    private const int Slots = 64;

    // The pictures inside, oldest first, in a ring
    private readonly long[] _timestamps = new long[Slots];
    private readonly long[] _entered = new long[Slots];
    private readonly LatencyWindow _durations = new();
    private readonly Lock _lock = new();
    private int _oldest;
    private int _waiting;

    // The fewest pictures left inside after one came out, matched in order
    private int _fewestWaiting = Slots;
    private long _in;
    private long _out;
    private volatile bool _isDisposed;

    private GstCodecTimer()
    {
    }

    /// <summary>Attaches to the "sink" and "src" pads of the element.</summary>
    /// <returns>Null when the element lacks one of them.</returns>
    public static GstCodecTimer? Attach(Element element)
    {
        var sink = gst_element_get_static_pad(element.Handle, "sink");
        var source = gst_element_get_static_pad(element.Handle, "src");

        if (sink == IntPtr.Zero || source == IntPtr.Zero)
        {
            if (sink != IntPtr.Zero)
                gst_object_unref(sink);

            if (source != IntPtr.Zero)
                gst_object_unref(source);

            return null;
        }

        var timer = new GstCodecTimer();

        // Each probe frees its handle when the pad drops it
        gst_pad_add_probe(sink, ProbeBuffer, &OnEnter, GCHandle.ToIntPtr(GCHandle.Alloc(timer, GCHandleType.Weak)),
            &FreeHandle);
        gst_pad_add_probe(source, ProbeBuffer, &OnLeave, GCHandle.ToIntPtr(GCHandle.Alloc(timer, GCHandleType.Weak)),
            &FreeHandle);
        gst_object_unref(sink);
        gst_object_unref(source);

        return timer;
    }

    /// <summary>The pictures that went in and came out since the previous take, and how long they took.</summary>
    public (long In, long Out, LatencyStats Durations) Take()
    {
        long entered;
        long left;

        lock (_lock)
        {
            entered = _in;
            left = _out;
            _in = 0;
            _out = 0;
        }

        return (entered, left, _durations.Take());
    }

    private void Enter(long timestamp)
    {
        lock (_lock)
        {
            _in++;

            // An element that keeps more is not one this measures; the oldest go
            if (_waiting == Slots)
                Forget(1);

            var slot = (_oldest + _waiting) % Slots;
            _timestamps[slot] = timestamp;
            _entered[slot] = Stopwatch.GetTimestamp();
            _waiting++;
        }
    }

    private void Leave(long timestamp)
    {
        long entered;

        lock (_lock)
        {
            _out++;

            if (_waiting == 0)
                return;

            var index = timestamp == ClockTimeNone ? -1 : Find(timestamp);

            if (index >= 0)
            {
                // The ones that went in before it were dropped
                Forget(index);
                entered = _entered[_oldest];
                Forget(1);
            }
            else
            {
                entered = _entered[_oldest];
                Forget(1);
                _fewestWaiting = Math.Min(_fewestWaiting, _waiting);
                Forget(_waiting - _fewestWaiting);
            }
        }

        _durations.Add(Stopwatch.GetElapsedTime(entered).TotalMilliseconds);
    }

    // Under the lock
    private int Find(long timestamp)
    {
        for (var index = 0; index < _waiting; index++)
        {
            if (_timestamps[(_oldest + index) % Slots] == timestamp)
                return index;
        }

        return -1;
    }

    // Under the lock
    private void Forget(int count)
    {
        _oldest = (_oldest + count) % Slots;
        _waiting -= count;
    }

    private static long PtsOf(IntPtr info)
    {
        var buffer = gst_pad_probe_info_get_buffer(info);
        return buffer == IntPtr.Zero ? ClockTimeNone : Marshal.ReadInt64(buffer, BufferPtsOffset);
    }

    private static GstCodecTimer? TimerOf(IntPtr data) =>
        GCHandle.FromIntPtr(data).Target is GstCodecTimer { _isDisposed: false } timer ? timer : null;

    [UnmanagedCallersOnly]
    private static void FreeHandle(IntPtr data) => GCHandle.FromIntPtr(data).Free();

    [UnmanagedCallersOnly]
    private static int OnEnter(IntPtr pad, IntPtr info, IntPtr data)
    {
        try
        {
            TimerOf(data)?.Enter(PtsOf(info));
        }
        catch (Exception)
        {
            // Nothing may leave a streaming thread
        }

        return ProbeOk;
    }

    [UnmanagedCallersOnly]
    private static int OnLeave(IntPtr pad, IntPtr info, IntPtr data)
    {
        try
        {
            TimerOf(data)?.Leave(PtsOf(info));
        }
        catch (Exception)
        {
            // Nothing may leave a streaming thread
        }

        return ProbeOk;
    }

    /// <summary>Stops counting; the probes go with the pads.</summary>
    public void Dispose() => _isDisposed = true;
}

/// <summary>Counts the buffers that pass a pad, for the rate of a capture.</summary>
/// <remarks>Held weakly by its probe, like <see cref="GstCodecTimer"/>.</remarks>
public sealed unsafe class GstBufferCounter
{
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_element_get_static_pad(IntPtr element, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_pad_add_probe(IntPtr pad, int mask,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> callback, IntPtr data,
        delegate* unmanaged<IntPtr, void> destroy);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_object_unref(IntPtr instance);

    private const int ProbeBuffer = 1 << 4;
    private const int ProbeOk = 1;

    private long _count;

    private GstBufferCounter()
    {
    }

    /// <returns>Null when the element has no such pad.</returns>
    public static GstBufferCounter? Attach(Element element, string padName)
    {
        var pad = gst_element_get_static_pad(element.Handle, padName);

        if (pad == IntPtr.Zero)
            return null;

        var counter = new GstBufferCounter();
        gst_pad_add_probe(pad, ProbeBuffer, &OnBuffer, GCHandle.ToIntPtr(GCHandle.Alloc(counter, GCHandleType.Weak)),
            &FreeHandle);
        gst_object_unref(pad);

        return counter;
    }

    /// <summary>The buffers since the previous take.</summary>
    public long Take() => Interlocked.Exchange(ref _count, 0);

    [UnmanagedCallersOnly]
    private static int OnBuffer(IntPtr pad, IntPtr info, IntPtr data)
    {
        if (GCHandle.FromIntPtr(data).Target is GstBufferCounter counter)
            Interlocked.Increment(ref counter._count);

        return ProbeOk;
    }

    [UnmanagedCallersOnly]
    private static void FreeHandle(IntPtr data) => GCHandle.FromIntPtr(data).Free();
}
