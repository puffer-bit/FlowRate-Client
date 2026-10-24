using Shared.Enums.WebRTC;
using Shared.Models.DataTransferObjects;

namespace Client.Core.Sessions.Hybrid;

/// <summary>
/// How the media of a hybrid session travel: codecs and the ways lost packets are made up for.
/// Bitrate, frame rate and resolution are not here; they are picked when a stream starts.
/// </summary>
/// <remarks>Shown and changed in the session settings, not yet applied to streams.</remarks>
public sealed record MediaSettings(
    WebRTCVideoCodecs CameraCodec = WebRTCVideoCodecs.VP8,
    WebRTCVideoCodecs ScreenCodec = WebRTCVideoCodecs.H264,
    WebRTCAudioCodecs AudioCodec = WebRTCAudioCodecs.Opus,
    bool IsVideoNackEnabled = true,
    bool IsVideoRtxEnabled = true,
    int KeyFrameRequestDelayMs = 1000,
    bool IsAudioNackEnabled = false,
    bool IsOpusFecEnabled = true,
    int OpusPacketLossPercent = 10,
    bool IsVoiceDtxEnabled = true,
    int VoiceJitterBufferMs = 100,
    int VideoJitterBufferMs = 200)
{
    public static MediaSettings Default { get; } = new();

    /// <summary>The video codecs a session may pick; the server refuses others.</summary>
    public static IReadOnlyList<WebRTCVideoCodecs> SupportedVideoCodecs => HybridMediaSettingsDTO.SupportedVideoCodecs;

    /// <summary>The audio codecs a session may pick; the server refuses others.</summary>
    public static IReadOnlyList<WebRTCAudioCodecs> SupportedAudioCodecs => HybridMediaSettingsDTO.SupportedAudioCodecs;
}
