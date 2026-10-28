using Client.Core.Media;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// The video streams a platform receives over libwebrtc, which puts their pictures together from the
/// packets but does not decode them: it hands them out encoded, for the decoders of
/// <see cref="Client.Application.Ports.IVideoMedia"/>, which draw on the GPU.
/// </summary>
public interface IEncodedVideoReceiver
{
    /// <summary>
    /// Takes the pictures of the stream with the given SSRC, from its first keyframe on, until the
    /// result is disposed. They come on a thread of libwebrtc, one per stream, and their data is only
    /// valid during the call.
    /// </summary>
    IDisposable Receive(uint ssrc, Action<EncodedVideoFrame> onFrame);

    /// <summary>Asks the sender of the stream for a keyframe. Any thread.</summary>
    void RequestKeyFrame(uint ssrc);
}
