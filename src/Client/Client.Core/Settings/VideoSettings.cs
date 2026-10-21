namespace Client.Core.Settings;

/// <summary>
/// User preferences of the Video tab: how screens are decoded, shown and encoded.
/// </summary>
public sealed record VideoSettings
{
    public static IReadOnlyList<int> PresentationBufferChoices { get; } = [1, 2, 3, 4];

    /// <summary>
    /// Decoding on the GPU when the system has a decoder for it. Off, the software decoder is used
    /// and frames are shown the way they always were.
    /// </summary>
    public bool IsHardwareDecodingEnabled { get; init; } = true;

    /// <summary>The decoder element the user picked, or null to choose it automatically.</summary>
    public string? Decoder { get; init; }

    /// <summary>The encoder element the user picked, or null to choose it automatically.</summary>
    public string? Encoder { get; init; }

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

    /// <summary>The count of shown and dropped frames is drawn over every screen.</summary>
    public bool ShowFrameStats { get; init; }

    /// <summary>
    /// The same settings with every value that cannot be chosen replaced by its default.
    /// </summary>
    public VideoSettings Normalize() => this with
    {
        PresentationBufferFrames = PresentationBufferChoices.Contains(PresentationBufferFrames)
            ? PresentationBufferFrames
            : new VideoSettings().PresentationBufferFrames,
        Decoder = string.IsNullOrWhiteSpace(Decoder) ? null : Decoder,
        Encoder = string.IsNullOrWhiteSpace(Encoder) ? null : Encoder
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
