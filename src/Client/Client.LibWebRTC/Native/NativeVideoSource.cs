using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.LibWebRTC.Voice;

namespace Client.LibWebRTC.Native;

/// <summary>
/// A screen we send over flowrate_webrtc: an fr_video_source, whose pictures the capture encodes.
/// </summary>
internal sealed unsafe class NativeVideoSource : IVideoSendSource
{
    private readonly IVideoSendListener _listener;
    private readonly Lock _lock = new();
    private GCHandle _self;
    private nint _source;

    // The rounds of negotiation that hand the source to the peer now; it is destroyed after the last
    private int _uses;
    private bool _isDisposed;

    private NativeVideoSource(IVideoSendListener listener) => _listener = listener;

    /// <param name="width">The size libwebrtc takes the stream for; the pictures keep their own.</param>
    /// <returns>Null when libwebrtc is not loaded.</returns>
    public static NativeVideoSource? Create(int width, int height, int maxFramerate, IVideoSendListener listener)
    {
        var source = new NativeVideoSource(listener);
        source._self = GCHandle.Alloc(source);

        var callbacks = new NativeMethods.VideoSourceCallbacks { Bitrate = &OnBitrate, KeyFrame = &OnKeyFrame };

        // libwebrtc copies the callbacks, so they may live on the stack
        source._source = NativeMethods.VideoSourceCreate(&callbacks, GCHandle.ToIntPtr(source._self), width, height,
            maxFramerate);

        if (source._source != 0)
            return source;

        source._self.Free();
        return null;
    }

    public void Push(EncodedVideoFrame frame)
    {
        lock (_lock)
        {
            if (!_isDisposed)
                NativeMethods.VideoSourcePush(_source, (byte*)frame.Data, frame.Length, frame.IsKeyFrame ? 1 : 0);
        }
    }

    /// <summary>
    /// The handle for one round of negotiation, which holds a reference of its own to it from then on;
    /// 0 once the source is disposed. Pair with <see cref="Release"/>.
    /// </summary>
    public nint Acquire()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return 0;

            _uses++;
            return _source;
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            if (--_uses == 0 && _isDisposed)
                Destroy();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_uses == 0)
                Destroy();
        }
    }

    // Inside the lock. Waits for a callback that runs, which never takes the lock
    private void Destroy()
    {
        NativeMethods.VideoSourceDestroy(_source);
        _self.Free();
    }

    private static IVideoSendListener? ListenerOf(nint context) =>
        GCHandle.FromIntPtr(context).Target is NativeVideoSource source ? source._listener : null;

    // On the encoder thread of libwebrtc, where an exception may not go
    [UnmanagedCallersOnly]
    private static void OnBitrate(nint context, int kbps)
    {
        try
        {
            ListenerOf(context)?.OnBitrate(kbps);
        }
        catch (Exception)
        {
            // The next estimate comes in a moment
        }
    }

    [UnmanagedCallersOnly]
    private static void OnKeyFrame(nint context)
    {
        try
        {
            ListenerOf(context)?.OnKeyFrameRequested();
        }
        catch (Exception)
        {
            // libwebrtc asks again
        }
    }
}
