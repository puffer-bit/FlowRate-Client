using Shared.Validation;

namespace Client.Core.Chat;

/// <summary>
/// What a video is compressed to, named by its shorter side like "720p".
/// </summary>
/// <param name="ShortSide">The shorter side of the picture; the longer one is up to 16:9 of it.</param>
/// <param name="VideoKbps">The video bitrate at up to 30 frames per second.</param>
/// <param name="MaxFrameRate">A faster video is reduced to it.</param>
public sealed record VideoCompressionPreset(int ShortSide, int VideoKbps, int MaxFrameRate)
{
    public const int AudioKbps = Limits.ChatVideoAudioKbps;

    public static VideoCompressionPreset Low { get; } = For(480);
    public static VideoCompressionPreset Medium { get; } = For(720);
    public static VideoCompressionPreset High { get; } = For(1080);

    /// <summary>
    /// The preset of <paramref name="shortSide"/>, with the bitrate the server measures compressed videos by.
    /// 60 frames per second are kept from 720p up; below, the bits go further at 30.
    /// </summary>
    public static VideoCompressionPreset For(int shortSide) =>
        new(shortSide, Limits.ChatVideoKbps(shortSide), shortSide >= 720 ? 60 : 30);

    public int LongSide => ShortSide * 16 / 9;

    /// <summary>
    /// The size a picture of <paramref name="width"/> × <paramref name="height"/> is reduced to, never enlarged,
    /// in even numbers as the encoders take them.
    /// </summary>
    public (int Width, int Height) Fit(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The picture has no size.");
        var scale = Math.Min(1, Math.Min((double)ShortSide / Math.Min(width, height),
            (double)LongSide / Math.Max(width, height)));
        return (Even(width * scale), Even(height * scale));
    }

    /// <summary>
    /// The frame rate a video of <paramref name="frameRate"/> is encoded at; an unknown rate (0) is taken as 30.
    /// </summary>
    public int FrameRateFor(double frameRate) =>
        frameRate <= 0 ? Math.Min(30, MaxFrameRate) : Math.Clamp((int)Math.Round(frameRate), 1, MaxFrameRate);

    /// <summary>
    /// The video bitrate at <paramref name="frameRate"/>: half as much again at 60 frames per second.
    /// </summary>
    public int VideoKbpsAt(int frameRate) =>
        frameRate <= 30 ? VideoKbps : (int)(VideoKbps * (1 + 0.5 * Math.Min(30, frameRate - 30) / 30));

    // Down, so a size is never above the original
    private static int Even(double value) => Math.Max(2, (int)Math.Floor(value / 2 + 1e-9) * 2);
}

/// <summary>
/// How the coordinator asks videos to be compressed before they are sent.
/// </summary>
/// <param name="MaxSide">The shorter side compressed videos are reduced to, like 720 for 720p.</param>
/// <param name="AllowOriginals">Videos may go uncompressed; otherwise the server refuses one above <paramref name="MaxSide"/>.</param>
public sealed record VideoCompressionPolicy(int MaxSide, bool AllowOriginals)
{
    public static VideoCompressionPolicy Default { get; } = new(720, true);

    private static readonly int[] CommonSides = [480, 720, 1080];

    /// <summary>
    /// The policy the coordinator sent. A server without these settings sends zeros, and takes any video.
    /// </summary>
    public static VideoCompressionPolicy From(int maxSide, bool allowOriginals) =>
        maxSide <= 0 ? Default : new VideoCompressionPolicy(maxSide, allowOriginals);

    /// <summary>
    /// The sides a user may pick, smallest first: the usual ones and the one of the server, none above it
    /// when the server takes only compressed videos.
    /// </summary>
    public IReadOnlyList<int> Sides => CommonSides.Append(MaxSide).Distinct()
        .Where(side => AllowOriginals || side <= MaxSide).Order().ToList();

    /// <summary>The side a video is compressed to: the chosen one where the server takes it, its own otherwise.</summary>
    public int SideFor(int? chosen) => chosen is { } side && Sides.Contains(side) ? side : MaxSide;
}

/// <summary>
/// The streams of a video file, as far as its preparation depends on them.
/// </summary>
/// <param name="VideoCodec">"h264", "h265", "vp9", "av1"..., or null for a file without a picture.</param>
/// <param name="Width">Of the picture as it is stored: before its rotation, with square pixels.</param>
/// <param name="FrameRate">The average, 0 where the file does not say.</param>
/// <param name="AudioCodec">"aac", "mp3", "opus"..., or null for a silent video.</param>
/// <param name="IsMp4">An MP4 or QuickTime file.</param>
/// <param name="IsFastStart">Its index comes before the media, so it plays before it is all downloaded.</param>
/// <param name="Size">Of the file, in bytes.</param>
public sealed record VideoFacts(string? VideoCodec, int Width, int Height, double FrameRate, string? AudioCodec,
    TimeSpan Duration, bool IsMp4, bool IsFastStart, long Size)
{
    /// <summary>Of the whole file, in kbit/s; 0 where the duration is unknown.</summary>
    public int Kbps => Duration > TimeSpan.Zero ? (int)(Size * 8 / Duration.TotalSeconds / 1000) : 0;
}

public enum VideoPreparationKind
{
    /// <summary>Sent as it is.</summary>
    AsIs,

    /// <summary>The same streams put into a new MP4: no metadata, the index first.</summary>
    Remux,

    /// <summary>Decoded and encoded again to H.264 and AAC in MP4.</summary>
    Transcode
}

public static class VideoPreparationRules
{
    // Codecs an MP4 carries and the player of the chat plays. H.265 stays as it is when only the
    // metadata goes: compressing it would make it H.264
    private static readonly HashSet<string> Mp4Video = ["h264", "h265", "av1", "vp9"];
    private static readonly HashSet<string> Mp4Audio = ["aac", "mp3", "opus"];

    // How much a video may be above the bitrate of the preset and still be sent without encoding it again
    private const double BitrateTolerance = 1.3;

    /// <summary>
    /// What is done to a video to send it: encoded again only when it is above the preset, put into a new
    /// MP4 when only its metadata or its layout is in the way.
    /// </summary>
    /// <param name="compress">Reduce it to <paramref name="preset"/>.</param>
    /// <param name="stripMetadata">Without where and when it was taken and by what.</param>
    public static VideoPreparationKind Decide(VideoFacts facts, bool compress, bool stripMetadata,
        VideoCompressionPreset preset)
    {
        var canRemux = (facts.VideoCodec == null || Mp4Video.Contains(facts.VideoCodec)) &&
                       (facts.AudioCodec == null || Mp4Audio.Contains(facts.AudioCodec));

        if (compress && facts.VideoCodec != null && NeedsEncoding(facts, preset))
            return VideoPreparationKind.Transcode;

        // Only an MP4 is put into a new one unless it is compressed: another container is sent as it is
        if (!canRemux || (!facts.IsMp4 && !compress))
            return VideoPreparationKind.AsIs;

        return stripMetadata || !facts.IsMp4 || !facts.IsFastStart
            ? VideoPreparationKind.Remux
            : VideoPreparationKind.AsIs;
    }

    private static bool NeedsEncoding(VideoFacts facts, VideoCompressionPreset preset)
    {
        if (facts.VideoCodec != "h264" || (facts.AudioCodec != null && !Mp4Audio.Contains(facts.AudioCodec)))
            return true;

        var (width, height) = preset.Fit(facts.Width, facts.Height);
        if (width < facts.Width || height < facts.Height)
            return true;

        var frameRate = preset.FrameRateFor(facts.FrameRate);
        if (facts.FrameRate > frameRate + 1)
            return true;

        var kbps = preset.VideoKbpsAt(frameRate) + VideoCompressionPreset.AudioKbps;
        return facts.Kbps > kbps * BitrateTolerance;
    }
}
