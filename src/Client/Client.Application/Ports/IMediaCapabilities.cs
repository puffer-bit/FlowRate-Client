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

    /// <summary>Writable parameters of an available hardware AV1 encoder.</summary>
    IReadOnlyList<EncoderParameter> GetEncoderParameters(string encoder) => [];

    /// <summary>The valid bitrate override of the encoder that will be used for this share.</summary>
    EncoderPropertyOverride? GetScreenShareBitrateOverride(VideoCodec codec, VideoSettings settings)
    {
        // Custom parameters currently apply only to hardware AV1 encoders.
        if (codec != VideoCodec.AV1)
            return null;
        var available = ScreenShareAv1Encoders;
        var encoder = settings.Av1Encoder is { } preferred && available.Contains(preferred)
            ? preferred : available.FirstOrDefault();
        if (encoder == null)
            return null;
        var parameters = GetEncoderParameters(encoder);
        return settings.EncoderOverrides.LastOrDefault(value => value.Encoder == encoder
            && value.Name is "bitrate" or "target-bitrate"
            && parameters.Any(parameter => parameter.Name == value.Name && parameter.IsValid(value.Value)));
    }
}
