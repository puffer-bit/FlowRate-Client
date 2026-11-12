using Client.Core.Media;
using Client.Core.Settings;

namespace Client.Application.Ports;

/// <summary>
/// What the media components of this machine can do. Asked after they are loaded.
/// </summary>
public interface IMediaCapabilities
{
    /// <summary>
    /// Whether a shared screen is encoded by hardware; without it the encoding falls back to software.
    /// </summary>
    bool HasHardwareScreenShareEncoder { get; }

    /// <summary>
    /// The decoders of shared screens this system has, in the order they are preferred.
    /// </summary>
    IReadOnlyList<string> ScreenShareDecoders { get; }

    /// <summary>The available AV1 screen decoders, in order of preference.</summary>
    IReadOnlyList<string> ScreenShareAv1Decoders { get; }

    /// <summary>
    /// The encoders of shared screens this system has, in the order they are preferred.
    /// </summary>
    IReadOnlyList<string> ScreenShareEncoders { get; }

    /// <summary>The available AV1 screen encoders, in order of preference.</summary>
    IReadOnlyList<string> ScreenShareAv1Encoders { get; }

    /// <summary>
    /// Whether the sound of a shared screen can be captured: of a single application, or of the
    /// whole system without our own sound.
    /// </summary>
    bool CanCaptureScreenAudio { get; }

    /// <summary>The available screen encoders for a codec, in order of preference.</summary>
    IReadOnlyList<string> GetScreenShareEncoders(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => ScreenShareEncoders,
        VideoCodec.AV1 => ScreenShareAv1Encoders,
        _ => []
    };

    /// <summary>The available screen decoders for a codec, in order of preference.</summary>
    IReadOnlyList<string> GetScreenShareDecoders(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => ScreenShareDecoders,
        VideoCodec.AV1 => ScreenShareAv1Decoders,
        _ => []
    };

    /// <summary>Writable parameters of an available screen encoder or decoder.</summary>
    IReadOnlyList<CodecParameter> GetCodecParameters(string factory) => [];

    /// <summary>The valid bitrate override of the encoder that will be used for this share.</summary>
    CodecPropertyOverride? GetScreenShareBitrateOverride(VideoCodec codec, VideoSettings settings)
    {
        if (!MediaRuntime.IsEnabled)
            return null;
        var available = GetScreenShareEncoders(codec);
        var encoder = settings.GetEncoder(codec) is { } preferred && available.Contains(preferred)
            ? preferred : available.FirstOrDefault();
        if (encoder == null)
            return null;
        var parameters = GetCodecParameters(encoder);
        return CodecParameter.ValidateOverrides(encoder, parameters, settings.CodecOverrides)
            .LastOrDefault(value => value.Name is "bitrate" or "target-bitrate");
    }
}
