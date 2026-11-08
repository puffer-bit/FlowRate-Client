using Client.Core.Media;

namespace Client.GStreamer.Video;

/// <summary>Encoded frame boundaries shared by capture, decoding and RTP transport.</summary>
public sealed record VideoCodecPipeline(string Caps, string? Parser, string Payloader, string Depayloader,
    string PayloaderOptions = "", string? EncoderCaps = null)
{
    public string Parse => Parser is { } parser ? parser + " ! " : "";

    public string Capture => (EncoderCaps is { } caps ? caps + " ! " : "") + Parse + Caps;

    public static VideoCodecPipeline For(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => H264,
        VideoCodec.VP8 => Vp8,
        VideoCodec.AV1 => Av1,
        VideoCodec.VP9 => Vp9,
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
    };

    // The SFU announces constrained baseline; parameter sets accompany every keyframe for late viewers.
    private static readonly VideoCodecPipeline H264 = new(
        "video/x-h264,stream-format=byte-stream,alignment=au", "h264parse config-interval=-1",
        "rtph264pay", "rtph264depay", "config-interval=-1 aggregate-mode=zero-latency",
        "video/x-h264,profile=constrained-baseline");
    private static readonly VideoCodecPipeline Vp8 = new("video/x-vp8", null,
        "rtpvp8pay", "rtpvp8depay", "picture-id-mode=15-bit");
    private static readonly VideoCodecPipeline Av1 = new(
        "video/x-av1,parsed=true,stream-format=obu-stream,alignment=tu", "av1parse", "rtpav1pay", "rtpav1depay");
    private static readonly VideoCodecPipeline Vp9 = new("video/x-vp9", null,
        "rtpvp9pay", "rtpvp9depay", "picture-id-mode=15-bit");
}
