namespace Client.Core.Media;

/// <summary>
/// How a screen is shared, as the user set it up before starting. It travels from the dialog to the
/// start of the share, and the last choice is stored for the next one.
/// </summary>
public sealed record ScreenShareSettings
{
    public const int DefaultMaxHeight = 1080;
    public const int DefaultFramerate = 30;

    /// <summary>
    /// The largest bitrate that can be set, in kbit/s. Every encoder takes a value up to it.
    /// </summary>
    public const int MaxBitrateKbps = 999_999;

    // What a picture of 1080 lines takes at 30 frames per second, the others are counted from it
    private const int ReferenceBitrateKbps = 4000;
    private const int ReferenceHeight = 1080;
    private const int ReferenceFramerate = 30;
    private const int MinRecommendedBitrateKbps = 500;

    // How far a bitrate may be from the recommended one before the picture or the viewers suffer
    private const double LowBitrateFactor = 0.5;
    private const double HighBitrateFactor = 3.0;

    /// <summary>
    /// The heights the user chooses from, in pixels.
    /// </summary>
    public static IReadOnlyList<int> MaxHeights { get; } = [480, 720, 1080];

    /// <summary>
    /// The frame rates the user chooses from, in frames per second.
    /// </summary>
    public static IReadOnlyList<int> Framerates { get; } = [15, 30, 60, 144];

    /// <summary>
    /// Rates above this one load the GPUs and the network of everyone involved and are worth a warning.
    /// </summary>
    public const int HighFramerateThreshold = 60;

    /// <summary>
    /// The height a larger picture is scaled down to, keeping its proportions. A smaller one is sent as it is.
    /// </summary>
    public int MaxHeight { get; init; } = DefaultMaxHeight;

    /// <summary>
    /// Frames sent per second.
    /// </summary>
    public int Framerate { get; init; } = DefaultFramerate;

    public bool IsHighFramerate => Framerate > HighFramerateThreshold;

    /// <summary>
    /// The bitrate the user set, in kbit/s, or null to use <see cref="RecommendedBitrateKbps"/>. It is
    /// taken as it is, however far from the recommended one.
    /// </summary>
    public int? BitrateKbps { get; init; }

    /// <summary>
    /// The sound goes along with the picture: of the shown window, of the whole system for a
    /// monitor, or on Linux of the application in <see cref="AudioAppKey"/>.
    /// </summary>
    public bool IsAudioEnabled { get; init; }

    /// <summary>
    /// The application whose sound is shared on Linux, or null for none.
    /// </summary>
    public string? AudioAppKey { get; init; }

    /// <summary>
    /// The bitrate that suits a picture of this size and rate, in kbit/s.
    /// </summary>
    /// <remarks>
    /// It follows the number of pixels, which goes with the square of the height. Twice the frames
    /// take far less than twice the bits, since neighbouring frames of a screen differ little.
    /// </remarks>
    public int RecommendedBitrateKbps
    {
        get
        {
            var size = Math.Pow((double)MaxHeight / ReferenceHeight, 2);
            var rate = Math.Sqrt((double)Framerate / ReferenceFramerate);

            return Math.Max(MinRecommendedBitrateKbps, (int)Math.Round(ReferenceBitrateKbps * size * rate / 100) * 100);
        }
    }

    /// <summary>
    /// The bitrate the encoder is given, in kbit/s.
    /// </summary>
    public int EffectiveBitrateKbps => BitrateKbps ?? RecommendedBitrateKbps;

    /// <summary>
    /// Whether <paramref name="bitrateKbps"/> is too little for this size and rate to look good.
    /// </summary>
    public bool IsBitrateTooLow(int bitrateKbps) => bitrateKbps < RecommendedBitrateKbps * LowBitrateFactor;

    /// <summary>
    /// Whether <paramref name="bitrateKbps"/> is far more than this size and rate need.
    /// </summary>
    public bool IsBitrateTooHigh(int bitrateKbps) => bitrateKbps > RecommendedBitrateKbps * HighBitrateFactor;

    /// <summary>
    /// The same settings with every value that cannot be chosen replaced by its default.
    /// </summary>
    public ScreenShareSettings Normalize() => new()
    {
        MaxHeight = MaxHeights.Contains(MaxHeight) ? MaxHeight : DefaultMaxHeight,
        Framerate = Framerates.Contains(Framerate) ? Framerate : DefaultFramerate,
        BitrateKbps = BitrateKbps is > 0 and <= MaxBitrateKbps ? BitrateKbps : null,
        IsAudioEnabled = IsAudioEnabled,
        AudioAppKey = string.IsNullOrWhiteSpace(AudioAppKey) ? null : AudioAppKey
    };
}
