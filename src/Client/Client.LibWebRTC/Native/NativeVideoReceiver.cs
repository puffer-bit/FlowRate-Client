using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.LibWebRTC.Voice;

namespace Client.LibWebRTC.Native;

/// <summary>
/// The video streams of every peer of the process: flowrate_webrtc.h has one receiver for them all,
/// and its pictures are told apart by their SSRC, which the SFU gives each consumer of its own.
/// </summary>
/// <remarks>Use only once <see cref="LibWebRTCRuntime.TryLoad"/> succeeded.</remarks>
public sealed unsafe class NativeVideoReceiver : IEncodedVideoReceiver
{
    private static readonly ConcurrentDictionary<uint, Action<EncodedVideoFrame>> Streams = new();
    private static int _isInstalled;

    public IDisposable Receive(uint ssrc, Action<EncodedVideoFrame> onFrame)
    {
        if (Interlocked.Exchange(ref _isInstalled, 1) == 0)
            NativeMethods.SetVideoReceiver(&OnFrame, 0);

        Streams[ssrc] = onFrame;
        return new Subscription(ssrc, onFrame);
    }

    public void RequestKeyFrame(uint ssrc) => NativeMethods.VideoRequestKeyFrame(ssrc);

    // On the decoding thread of the stream, where an exception would tear the process down; the
    // receivers catch their own
    [UnmanagedCallersOnly]
    private static void OnFrame(nint context, uint ssrc, byte* data, int size, long timestampNs, int isKeyFrame)
    {
        try
        {
            if (Streams.TryGetValue(ssrc, out var onFrame))
                onFrame(new EncodedVideoFrame((nint)data, size, timestampNs, isKeyFrame != 0));
        }
        catch (Exception)
        {
            // Nothing to tell it to
        }
    }

    private sealed class Subscription(uint ssrc, Action<EncodedVideoFrame> onFrame) : IDisposable
    {
        // Only its own: a new consumer may have taken the SSRC over in the meantime
        public void Dispose() => Streams.TryRemove(new KeyValuePair<uint, Action<EncodedVideoFrame>>(ssrc, onFrame));
    }
}
