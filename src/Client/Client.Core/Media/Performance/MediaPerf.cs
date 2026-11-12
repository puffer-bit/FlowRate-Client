using Shared.Models.Media;

namespace Client.Core.Media;

/// <summary>
/// What the encoder of a screen we show did over a window: how many pictures came from the capture and
/// how many it put out, how long each took it, and what it made of them.
/// </summary>
/// <param name="Element">The element that encodes, such as <c>nvd3d11h264enc</c>.</param>
/// <param name="Scaler">Where the picture is scaled and converted for it, such as <c>GPU (VA-API)</c>.</param>
/// <param name="CapturedPerSecond">Pictures the capture delivered, before the pacing to the frame rate.</param>
/// <param name="InputPerSecond">Pictures that reached the encoder.</param>
/// <param name="OutputPerSecond">Pictures the encoder put out.</param>
/// <param name="EncodeMs">From a picture entering the encoder to it coming out encoded.</param>
/// <param name="BitrateKbps">What the encoded pictures came to.</param>
/// <param name="TargetKbps">The bitrate the encoder was last given.</param>
/// <param name="KeyFrames">Keyframes made in the window.</param>
/// <param name="MaxKeyFrameBytes">The largest keyframe of the window, 0 without one.</param>
/// <param name="AverageFrameBytes">The average size of the other pictures.</param>
public sealed record EncoderPerf(
    string Element,
    string Scaler,
    double CapturedPerSecond,
    double InputPerSecond,
    double OutputPerSecond,
    LatencyStats EncodeMs,
    double BitrateKbps,
    int TargetKbps,
    int KeyFrames,
    int MaxKeyFrameBytes,
    int AverageFrameBytes);

/// <summary>What the decoder of a screen we watch did over a window.</summary>
/// <param name="Element">The element that decodes, such as <c>vah264dec</c>.</param>
/// <param name="InputPerSecond">Encoded pictures pushed into it.</param>
/// <param name="OutputPerSecond">Pictures it put out.</param>
/// <param name="DecodeMs">From a picture entering the decoder to it coming out decoded.</param>
/// <param name="Queued">The encoded pictures waiting for it at the last push.</param>
/// <param name="MaxQueued">The most that waited in the window.</param>
/// <param name="Dropped">Pictures dropped in the window because it fell behind, up to a keyframe.</param>
public sealed record DecoderPerf(
    string Element,
    bool IsHardware,
    double InputPerSecond,
    double OutputPerSecond,
    LatencyStats DecodeMs,
    int Queued,
    int MaxQueued,
    long Dropped);

/// <summary>
/// How a picture we receive fared in the transport over a window, from its packets to it being handed
/// to the decoder.
/// </summary>
/// <param name="JitterBufferMs">How long a picture waited in the jitter buffer on average.</param>
/// <param name="JitterBufferTargetMs">How long the jitter buffer aims to hold them, where it is reported.</param>
/// <param name="AssemblyMs">From the first to the last packet of a picture that took several, on average.</param>
/// <param name="ProcessingMs">From the first packet of a picture to it being decoded, on average.</param>
/// <param name="ReceivedPerSecond">Pictures put together from packets.</param>
/// <param name="DecodedPerSecond">Pictures handed to the decoder.</param>
/// <param name="Dropped">Pictures the transport dropped in the window.</param>
/// <param name="Freezes">Freezes of the picture in the window, as WebRTC counts them.</param>
/// <param name="FreezeMs">How long they lasted together.</param>
/// <param name="Plis">The keyframes we asked for.</param>
/// <param name="Firs">The full intra requests we sent.</param>
/// <param name="KeyFrames">Keyframes received.</param>
/// <param name="DecoderImplementation">What decodes, where libwebrtc decodes itself (Android).</param>
/// <param name="DecodeMs">How long a picture took libwebrtc to decode on average, where it decodes itself.</param>
/// <param name="WaitMs">
/// From the first packet of a picture to it leaving libwebrtc, measured by flowrate_webrtc; null where
/// the library does not measure it.
/// </param>
/// <param name="HandoffMs">How long handing a picture to the decoder took, measured by flowrate_webrtc.</param>
public sealed record VideoReceivePerf(
    double BitrateKbps,
    long Packets,
    double LossPercent,
    double JitterMs,
    double JitterBufferMs,
    double? JitterBufferTargetMs,
    double AssemblyMs,
    double ProcessingMs,
    double ReceivedPerSecond,
    double DecodedPerSecond,
    long Dropped,
    int Freezes,
    double FreezeMs,
    int Nacks,
    int Plis,
    int Firs,
    int KeyFrames,
    int Width,
    int Height,
    string? DecoderImplementation = null,
    double? DecodeMs = null,
    TimingStats? WaitMs = null,
    TimingStats? HandoffMs = null);

/// <summary>How a screen we show fared in the transport over a window.</summary>
/// <param name="TargetKbps">What the bandwidth estimate gives the stream.</param>
/// <param name="SentPerSecond">Pictures sent.</param>
/// <param name="PacerMs">How long a packet waited in the pacer on average.</param>
/// <param name="Retransmitted">Packets sent again.</param>
/// <param name="HugeFrames">Pictures far larger than the average, as WebRTC counts them.</param>
/// <param name="QualityLimitation">What WebRTC says holds the stream back: none, bandwidth, cpu or other.</param>
/// <param name="LossPercent">The loss the SFU reports of the stream.</param>
/// <param name="RoundTripMs">The round trip of the receiver reports of the stream.</param>
/// <param name="QueueMs">
/// From a picture being pushed into libwebrtc to its encoder taking it, measured by flowrate_webrtc;
/// null where the library does not measure it.
/// </param>
/// <param name="Skipped">Pictures pushed that libwebrtc never sent on, measured by flowrate_webrtc.</param>
public sealed record VideoSendPerf(
    double BitrateKbps,
    double? TargetKbps,
    double SentPerSecond,
    double PacerMs,
    long Retransmitted,
    int Nacks,
    int Plis,
    int Firs,
    int HugeFrames,
    string? QualityLimitation,
    double? LossPercent,
    double? RoundTripMs,
    TimingStats? QueueMs = null,
    long? Skipped = null);

/// <summary>How a sound we receive fared in the jitter buffer of WebRTC (NetEq) over a window.</summary>
/// <param name="JitterBufferMs">How long a sample waited on average.</param>
/// <param name="JitterBufferTargetMs">How long the buffer aims to hold them.</param>
/// <param name="ConcealedPercent">The share of the samples made up for lost or late packets.</param>
/// <param name="AcceleratedPercent">The share removed to catch up with a buffer grown too long.</param>
/// <param name="DeceleratedPercent">The share added to stretch a buffer running dry.</param>
/// <param name="FecPackets">Packets recovered with forward error correction.</param>
public sealed record AudioReceivePerf(
    double BitrateKbps,
    double LossPercent,
    double JitterMs,
    double JitterBufferMs,
    double? JitterBufferTargetMs,
    double ConcealedPercent,
    double AcceleratedPercent,
    double DeceleratedPercent,
    long FecPackets);

/// <summary>The connection to the SFU over a window.</summary>
/// <param name="RoundTripMs">The round trip of the candidate pair in use.</param>
/// <param name="AvailableOutgoingKbps">What the bandwidth estimate lets us send.</param>
/// <param name="SentKbps">What went out over the pair, or null where the transport does not count it.</param>
/// <param name="ReceivedKbps">What came in over the pair, or null where the transport does not count it.</param>
/// <param name="IsOverTcp">The media goes over TCP, or null while no pair is selected.</param>
/// <param name="LocalCandidate">The type of our candidate in use: host, srflx, prflx or relay.</param>
/// <param name="RemoteCandidate">The type of the candidate of the SFU.</param>
public sealed record NetworkPerf(
    double? RoundTripMs,
    double? AvailableOutgoingKbps,
    double? SentKbps,
    double? ReceivedKbps,
    bool? IsOverTcp,
    string? LocalCandidate = null,
    string? RemoteCandidate = null);

/// <summary>
/// One stream of a user over a window: a screen we watch, with its sound, or the screen we show.
/// Every part is null where the transport does not have it.
/// </summary>
/// <param name="IsOwn">Our own screen, which we send rather than receive.</param>
public sealed record StreamPerf(
    int UserId,
    MediaSourceType SourceType,
    bool IsOwn,
    VideoReceivePerf? Receive = null,
    DecoderPerf? Decoder = null,
    VideoSendPerf? Send = null,
    EncoderPerf? Encoder = null,
    AudioReceivePerf? Audio = null);

/// <summary>Everything the transport measured of its media over a window.</summary>
/// <param name="Engine">The engine of the transport, libwebrtc or GStreamer.</param>
/// <param name="Window">How long the window was.</param>
/// <param name="Voices">The voices we receive, by user.</param>
public sealed record MediaPerfReport(
    string Engine,
    TimeSpan Window,
    NetworkPerf? Network,
    IReadOnlyList<StreamPerf> Streams,
    IReadOnlyDictionary<int, AudioReceivePerf> Voices)
{
    public StreamPerf? Find(int userId, MediaSourceType sourceType) =>
        Streams.FirstOrDefault(stream => stream.UserId == userId && stream.SourceType == sourceType);
}
