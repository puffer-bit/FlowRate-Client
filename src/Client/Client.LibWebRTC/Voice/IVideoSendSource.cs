using Client.Core.Media;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// A screen we send whose pictures come encoded from the capture: libwebrtc packetizes, paces and
/// resends them, and asks the listener it was made with for the bitrate and the keyframes.
/// </summary>
public interface IVideoSendSource : IDisposable
{
    /// <summary>The next picture, in the codec of the section it is sent in. Any thread, one at a time.</summary>
    void Push(EncodedVideoFrame frame);
}

/// <summary>What libwebrtc wants of the encoder of a screen we send. Called on a thread of libwebrtc.</summary>
public interface IVideoSendListener
{
    /// <summary>The bitrate the network takes now, in kbit/s.</summary>
    void OnBitrate(int kbps);

    /// <summary>The next picture should be a keyframe, as a viewer joined or lost one.</summary>
    void OnKeyFrameRequested();
}

/// <summary>A section we send in, and what goes into it.</summary>
/// <param name="Ssrc">The SSRC the SFU expects there.</param>
/// <param name="Video">The screen of the section, or null for the microphone.</param>
public sealed record SendSection(string Mid, uint Ssrc, IVideoSendSource? Video = null);
