using System.Globalization;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.WebRTC.Sdp;
using Shared.Models.Media;

namespace Client.LibWebRTC.Transport;

/// <summary>
/// The sections we send in, as the description of the SFU offers them to libwebrtc. libwebrtc encodes
/// by what the receiver asks for in its description, and the receiver here is the SFU, described by
/// us: so these sections are what sets the codecs, FEC, DTX and the bitrates of what we send.
/// </summary>
internal static class SendSections
{
    // The bitrate of the voice
    private const int VoiceBitrate = 64000;

    // The sound of a screen, stereo at a bitrate that keeps music clean
    private const int ScreenSoundBitrate = 128000;

    private const string TransportCcFeedback = "transport-cc";

    /// <summary>
    /// A section we send in. Where the SFU answers with transport-cc feedback, every packet carries its
    /// number on the transport, and libwebrtc estimates the bandwidth by the delays the feedback
    /// reports. Where it does not, the section offers none: libwebrtc would take the missing feedback
    /// for a network that delivers nothing, and lower the bitrate of a screen to its least.
    /// </summary>
    /// <param name="transportCcExtensionId">The id of the transport-wide sequence number, 0 while the SFU gave none.</param>
    public static SdpMediaSection Section(string mid, string kind, RtpCodec codec, int transportCcExtensionId)
    {
        var section = transportCcExtensionId <= 0
            ? new SdpMediaSection { Mid = mid, Kind = kind, Direction = SdpDirection.RecvOnly, Codec = codec }
            : new SdpMediaSection
            {
                Mid = mid,
                Kind = kind,
                Direction = SdpDirection.RecvOnly,
                Codec = codec.RtcpFeedback.Contains(TransportCcFeedback)
                    ? codec
                    : codec with { RtcpFeedback = [..codec.RtcpFeedback, TransportCcFeedback] },
                HeaderExtensions = [new SdpHeaderExtension(transportCcExtensionId, SdpHeaderExtension.TransportWideCcUri)]
            };

        return RtpCodec.VideoCodecOf(codec.EncodingName) == VideoCodec.AV1
            ? section with { HeaderExtensions = [..section.HeaderExtensions,
                new SdpHeaderExtension(SdpHeaderExtension.Av1DependencyDescriptorId, SdpHeaderExtension.Av1DependencyDescriptorUri)] }
            : section;
    }

    /// <summary>
    /// The codec of a section we send a voice or the sound of a screen in: its format parameters set
    /// FEC, DTX and the bitrate.
    /// </summary>
    public static RtpCodec SoundCodecOf(OutboundTrack outbound) => RtpCodec.FromOutbound(outbound) with
    {
        // The sound of a screen is stereo, and never has DTX: a quiet passage of a film is not a pause
        FormatParameters = outbound.SourceType == MediaSourceType.Screen
            ? string.Create(CultureInfo.InvariantCulture,
                $"minptime=10;useinbandfec={(outbound.IsFecEnabled ? 1 : 0)};usedtx=0;stereo=1;sprop-stereo=1;maxaveragebitrate={ScreenSoundBitrate}")
            : string.Create(CultureInfo.InvariantCulture,
                $"minptime=10;useinbandfec={(outbound.IsFecEnabled ? 1 : 0)};usedtx={(outbound.IsDtxEnabled ? 1 : 0)};maxaveragebitrate={VoiceBitrate}")
    };

    /// <summary>
    /// The codec of a section we send a screen or a camera in. libwebrtc takes the bitrates of the remote
    /// description as the bounds of its estimate: it starts at the one the user set, never goes above
    /// it, and goes down when the network loses packets.
    /// </summary>
    public static RtpCodec VideoCodecOf(OutboundTrack outbound, OutgoingVideo video)
    {
        var codec = RtpCodec.FromOutbound(outbound);
        var bitrates = string.Create(CultureInfo.InvariantCulture,
            $"x-google-start-bitrate={video.Settings.EffectiveBitrateKbps};x-google-max-bitrate={video.Settings.EffectiveBitrateKbps};x-google-min-bitrate={video.MinKbps}");

        return codec with
        {
            FormatParameters = string.IsNullOrEmpty(codec.FormatParameters) ? bitrates : $"{codec.FormatParameters};{bitrates}",
            RtxPayloadType = RtxSsrcOf(outbound) != 0 ? outbound.RtxPayloadType : null
        };
    }

    /// <summary>
    /// The SSRC libwebrtc resends the lost packets of a screen on, as RTX: the SFU asks for them only
    /// where it gave the producer an RTX stream, and takes them only there. 0 where it gave none.
    /// </summary>
    public static uint RtxSsrcOf(OutboundTrack outbound) =>
        outbound.RtxPayloadType > 0 && outbound.RtxSSRC != 0 ? outbound.RtxSSRC : 0;
}
