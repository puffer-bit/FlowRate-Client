using System.Diagnostics;
using Client.Core.Media;
using Gst;

namespace Client.GStreamer.Interop;

/// <summary>
/// Pushes encoded frames into a leaky appsrc without letting it drop one on its own. A frame the
/// appsrc drops leaves no gap in the RTP sequence numbers or the stream of the decoder, so nothing
/// would ask for a keyframe, and the picture would stay broken until the encoder sends one by itself.
/// Here a full appsrc drops the frames up to the next keyframe instead, and the caller asks for it.
/// </summary>
/// <remarks>Not thread-safe: the frames of one appsrc come from one thread, or under a lock.</remarks>
public sealed class EncodedFrameGate(Element appsrc, ulong maxFrames)
{
    private const string LevelProperty = "current-level-buffers";

    // A keyframe the encoder sends at once would find the appsrc still full, be dropped and ask for
    // the next one: the encoder would make nothing but keyframes, and the log say so each time
    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(1);

    private bool _isWaitingForKeyFrame;
    private long _lastRequest = -1;

    /// <summary>The frames dropped so far.</summary>
    public long Dropped { get; private set; }

    /// <summary>
    /// Drops the frames up to the next keyframe, as after a new decoder, whose caller asks for the
    /// keyframe itself.
    /// </summary>
    public void WaitForKeyFrame()
    {
        _isWaitingForKeyFrame = true;
        _lastRequest = Stopwatch.GetTimestamp();
    }

    /// <returns>
    /// True when the caller should ask for a keyframe: at the start of a gap, and again at most
    /// once a second while it lasts.
    /// </returns>
    public bool Push(EncodedVideoFrame frame)
    {
        if (_isWaitingForKeyFrame && !frame.IsKeyFrame)
            return Drop();

        // Without room the keyframe would only push out the frames before it, and the appsrc would
        // still be full for the ones after it
        if (GstNative.GetUInt64(appsrc.Handle, LevelProperty) >= maxFrames)
        {
            _isWaitingForKeyFrame = true;
            return Drop();
        }

        _isWaitingForKeyFrame = false;
        GstEncodedNative.Push(appsrc, frame);
        return false;
    }

    private bool Drop()
    {
        Dropped++;

        if (_lastRequest >= 0 && Stopwatch.GetElapsedTime(_lastRequest) < RequestInterval)
            return false;

        _lastRequest = Stopwatch.GetTimestamp();
        return true;
    }
}
