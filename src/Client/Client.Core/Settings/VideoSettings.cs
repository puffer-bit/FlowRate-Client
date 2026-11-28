using Client.Core.Media;

namespace Client.Core.Settings;

/// <summary>
/// User preferences of the Video tab: how screens are decoded, shown and encoded.
/// </summary>
public sealed record VideoSettings
{
    /// <summary>Independent codec preferences for camera streams; null uses defaults.</summary>
    public VideoSettings? Camera { get; init; }

    public VideoSettings ForCamera() => (Camera ?? new VideoSettings()) with
    {
        Camera = null,
        IsZeroCopyEnabled = IsZeroCopyEnabled,
        IsTimedPresentationEnabled = IsTimedPresentationEnabled,
        PresentationBufferFrames = PresentationBufferFrames
    };

    public static IReadOnlyList<int> PresentationBufferChoices { get; } = [1, 2, 3, 4];

    /// <summary>
    /// Decoding on the GPU when the system has a decoder for it. Off, the software decoder is used
    /// and frames are shown the way they always were.
    /// </summary>
    public bool IsHardwareDecodingEnabled { get; init; } = true;

    /// <summary>The decoder element the user picked, or null to choose it automatically.</summary>
    public string? Decoder { get; init; }

    /// <summary>The AV1 decoder element, or null to choose it automatically.</summary>
    public string? Av1Decoder { get; init; }

    /// <summary>The encoder element the user picked, or null to choose it automatically.</summary>
    public string? Encoder { get; init; }

    /// <summary>The AV1 encoder element, or null to choose it automatically.</summary>
    public string? Av1Encoder { get; init; }

    /// <summary>The VP8 encoder element, or null to choose it automatically.</summary>
    public string? Vp8Encoder { get; init; }

    /// <summary>The VP9 encoder element, or null to choose it automatically.</summary>
    public string? Vp9Encoder { get; init; }

    /// <summary>The VP8 decoder element, or null to choose it automatically.</summary>
    public string? Vp8Decoder { get; init; }

    /// <summary>The VP9 decoder element, or null to choose it automatically.</summary>
    public string? Vp9Decoder { get; init; }

    public string? GetDecoder(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => Decoder,
        VideoCodec.AV1 => Av1Decoder,
        VideoCodec.VP8 => Vp8Decoder,
        VideoCodec.VP9 => Vp9Decoder,
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
    };

    public string? GetEncoder(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => Encoder,
        VideoCodec.AV1 => Av1Encoder,
        VideoCodec.VP8 => Vp8Encoder,
        VideoCodec.VP9 => Vp9Encoder,
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
    };

    public bool IsCodecSettingsWarningAcknowledged { get; init; }

    public IReadOnlyList<CodecPropertyOverride> CodecOverrides { get; init; } = [];

    /// <summary>
    /// Decoded frames go to the screen as GPU textures without passing through memory, where the
    /// system allows it.
    /// </summary>
    public bool IsZeroCopyEnabled { get; init; } = true;

    /// <summary>
    /// Frames are shown at the moment their timestamps call for, on the refresh of the monitor,
    /// rather than the moment they arrive.
    /// </summary>
    public bool IsTimedPresentationEnabled { get; init; } = true;

    /// <summary>How many frames wait to be shown, which evens out the network at the cost of delay.</summary>
    public int PresentationBufferFrames { get; init; } = 2;

    /// <summary>
    /// The same settings with every value that cannot be chosen replaced by its default.
    /// </summary>
    public VideoSettings Normalize() => this with
    {
        PresentationBufferFrames = PresentationBufferChoices.Contains(PresentationBufferFrames)
            ? PresentationBufferFrames
            : new VideoSettings().PresentationBufferFrames,
        Decoder = string.IsNullOrWhiteSpace(Decoder) ? null : Decoder,
        Av1Decoder = string.IsNullOrWhiteSpace(Av1Decoder) ? null : Av1Decoder,
        Encoder = string.IsNullOrWhiteSpace(Encoder) ? null : Encoder,
        Av1Encoder = string.IsNullOrWhiteSpace(Av1Encoder) ? null : Av1Encoder,
        Vp8Encoder = string.IsNullOrWhiteSpace(Vp8Encoder) ? null : Vp8Encoder,
        Vp9Encoder = string.IsNullOrWhiteSpace(Vp9Encoder) ? null : Vp9Encoder,
        Vp8Decoder = string.IsNullOrWhiteSpace(Vp8Decoder) ? null : Vp8Decoder,
        Vp9Decoder = string.IsNullOrWhiteSpace(Vp9Decoder) ? null : Vp9Decoder
    };

    /// <summary>
    /// The settings in effect, read by the media layers whenever they build a stream.
    /// </summary>
    public static VideoSettings Current { get; private set; } = new();

    /// <summary>Raised after <see cref="Current"/> changed, on the thread that changed it.</summary>
    public static event Action? Changed;

    public static void Apply(VideoSettings settings)
    {
        Current = settings.Normalize();
        Changed?.Invoke();
    }
}
