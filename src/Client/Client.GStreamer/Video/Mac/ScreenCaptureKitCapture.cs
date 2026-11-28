using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Video.Mac;

/// <summary>
/// A capture of ScreenCaptureKit that pushes what it takes into an appsrc: the pictures of a display or
/// a window, or the sound of applications. The system hands them over on a queue of its own, so they
/// are pushed with native calls only, which make no wrapper on that thread.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class ScreenCaptureKitCapture : IDisposable
{
    private const int ErrorSize = 512;

    [DllImport(NativeLibraries.GStreamer)]
    private static extern nint gst_buffer_new_memdup(nint data, nuint size);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern nint gst_caps_from_string([MarshalAs(UnmanagedType.LPUTF8Str)] string caps);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_caps_unref(nint caps);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern nint gst_object_ref(nint obj);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_object_unref(nint obj);

    [DllImport(NativeLibraries.GstApp)]
    private static extern int gst_app_src_push_buffer(nint appsrc, nint buffer);

    [DllImport(NativeLibraries.GstApp)]
    private static extern void gst_app_src_set_caps(nint appsrc, nint caps);

    // A reference of our own: the pipeline that holds the element may let it go before the capture stops
    private readonly nint _source;
    private readonly int _framerate;
    private readonly Action<Exception> _onError;
    private readonly Lock _lock = new();
    private GCHandle _handle;
    private nint _capture;
    private int _width;
    private int _height;

    private ScreenCaptureKitCapture(Element source, int framerate, Action<Exception> onError)
    {
        _source = gst_object_ref(source.Handle);
        _framerate = framerate;
        _onError = onError;
        _handle = GCHandle.Alloc(this);
    }

    /// <summary>
    /// Starts a capture into <paramref name="source"/>, an appsrc of BGRA pictures (the size is set from
    /// the first one) or of the sound of <see cref="AudioCaps"/>. Blocks until the system runs it.
    /// </summary>
    /// <param name="onError">Receives the end of a capture the system stopped by itself, on any thread.</param>
    /// <exception cref="InvalidOperationException">The capture did not start.</exception>
    public static ScreenCaptureKitCapture Start(Element source, ScreenCaptureKitNative.Config config,
        Action<Exception> onError)
    {
        var capture = new ScreenCaptureKitCapture(source, config.Framerate, onError);

        var callbacks = new ScreenCaptureKitNative.Callbacks
        {
            Video = &OnVideo,
            Audio = &OnAudio,
            Stopped = &OnStopped
        };

        var error = stackalloc byte[ErrorSize];
        error[0] = 0;
        var handle = ScreenCaptureKitNative.Start(&config, &callbacks, GCHandle.ToIntPtr(capture._handle), error, ErrorSize);

        if (handle == 0)
        {
            var message = Marshal.PtrToStringUTF8((nint)error);
            capture.Dispose();
            throw new InvalidOperationException($"ScreenCaptureKit did not start: {message}");
        }

        lock (capture._lock)
            capture._capture = handle;

        return capture;
    }

    /// <summary>The sound the capture hands over.</summary>
    public const string AudioCaps = "audio/x-raw,format=F32LE,layout=interleaved,rate=48000,channels=2";

    private void PushVideo(byte* data, int width, int height)
    {
        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;

            var caps = gst_caps_from_string(string.Format(CultureInfo.InvariantCulture,
                "video/x-raw,format=BGRA,width={0},height={1},framerate={2}/1,pixel-aspect-ratio=1/1",
                width, height, _framerate));

            if (caps != 0)
            {
                gst_app_src_set_caps(_source, caps);
                gst_caps_unref(caps);
            }
        }

        Push(data, (nuint)width * (nuint)height * 4);
    }

    private void Push(void* data, nuint size)
    {
        var buffer = gst_buffer_new_memdup((nint)data, size);

        // Takes the buffer; refused until the pipeline plays and after it stops
        if (buffer != 0)
            gst_app_src_push_buffer(_source, buffer);
    }

    private static ScreenCaptureKitCapture? From(nint ctx) => GCHandle.FromIntPtr(ctx).Target as ScreenCaptureKitCapture;

    [UnmanagedCallersOnly]
    private static void OnVideo(nint ctx, byte* data, int width, int height)
    {
        try
        {
            From(ctx)?.PushVideo(data, width, height);
        }
        catch (Exception exception)
        {
            From(ctx)?._onError(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnAudio(nint ctx, float* data, int frames, int channels)
    {
        try
        {
            From(ctx)?.Push(data, (nuint)frames * (nuint)channels * sizeof(float));
        }
        catch (Exception exception)
        {
            From(ctx)?._onError(exception);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnStopped(nint ctx, int error, byte* message)
    {
        try
        {
            From(ctx)?._onError(new InvalidOperationException(
                $"ScreenCaptureKit stopped the capture ({error}): {Marshal.PtrToStringUTF8((nint)message)}"));
        }
        catch
        {
            // Nothing may leave a callback of the system
        }
    }

    public void Dispose()
    {
        nint capture;

        lock (_lock)
        {
            if (!_handle.IsAllocated)
                return;

            capture = _capture;
            _capture = 0;
        }

        // Returns once no callback runs any more, so the handle and the element may go
        if (capture != 0)
            ScreenCaptureKitNative.Stop(capture);

        _handle.Free();
        gst_object_unref(_source);
    }
}
