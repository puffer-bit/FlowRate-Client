using Client.Core.Media;

namespace Client.Application.Ports;

/// <summary>
/// The pictures of shared screens apart from how they travel: capturing and encoding the one we show,
/// decoding the ones we watch into frames to draw. A transport, whatever engine it runs on, takes
/// encoded frames from here and gives encoded frames back, and owns nothing of the picture itself.
/// </summary>
public interface IVideoMedia
{
    /// <summary>
    /// Prepares the capture of a screen, encoded in <paramref name="codec"/>. Nothing is captured
    /// before <see cref="IScreenCapture.Start"/>.
    /// </summary>
    /// <param name="settings">The size, the rate and the bitrate of the picture.</param>
    /// <param name="name">Tells the capture apart in the log and names its threads.</param>
    /// <returns>Null when the input cannot be captured here or no encoder of the codec is there.</returns>
    IScreenCapture? CreateCapture(VideoInput input, ScreenShareSettings settings, VideoCodec codec, string name);

    /// <summary>Prepares a decoder of one stream; it runs from the first frame pushed into it.</summary>
    /// <param name="name">Tells the decoder apart in the log and names its threads.</param>
    /// <returns>Null when no decoder of the codec is there.</returns>
    IVideoDecoder? CreateDecoder(VideoCodec codec, string name);
}

/// <summary>
/// The capture of one shown screen. The events are raised on threads of the capture and the data of
/// a frame is only valid during the call; subscribe before <see cref="Start"/>.
/// </summary>
public interface IScreenCapture : IDisposable
{
    /// <summary>The encoder and where the picture is scaled, for the log.</summary>
    string Description { get; }

    /// <summary>What is sent, as the sharer sees it: at the size and rate of the capture, in memory.</summary>
    event Action<RawVideoFrame>? PreviewFrameReady;

    /// <summary>Every encoded picture, in the order to send it.</summary>
    event Action<EncodedVideoFrame>? EncodedFrameReady;

    /// <returns>False when the capture could not start.</returns>
    bool Start();

    /// <summary>Makes the next picture a keyframe, for a viewer who joined or lost one. Any thread.</summary>
    void RequestKeyFrame();

    /// <summary>Changes the bitrate of the encoder while it runs, where the encoder allows it.</summary>
    void SetBitrate(int kbps);

    /// <summary>
    /// Starts or stops the preview. A preview nobody looks at still costs the download of every frame
    /// from the GPU, so it is stopped while our own screen is not watched. Shown at the start. Any thread.
    /// </summary>
    void SetPreviewShown(bool isShown);
}

/// <summary>
/// The decoder of one watched stream. The frames are raised on a thread of the decoder and are only
/// valid during the call: a receiver copies the pixels or retains the GPU frame it keeps.
/// </summary>
public interface IVideoDecoder : IDisposable
{
    event Action<RawVideoFrame>? FrameReady;

    /// <summary>
    /// How the frames are decoded, raised before the first frame of the decoder and again before the
    /// first frame after it was rebuilt to deliver them differently. Raised on a thread of the decoder.
    /// </summary>
    event Action<VideoDecoding>? DecodingChanged;

    /// <summary>
    /// The decoder cannot go on without a keyframe, as after it was rebuilt; the transport asks the
    /// sender for one. Raised on a thread of the decoder.
    /// </summary>
    event Action? KeyFrameNeeded;

    /// <summary>Decodes the next picture of the stream. Called from one thread at a time.</summary>
    void Push(EncodedVideoFrame frame);
}
