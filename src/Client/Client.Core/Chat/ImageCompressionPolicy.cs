namespace Client.Core.Chat;

/// <summary>
/// How the coordinator asks images to be compressed before they are sent.
/// </summary>
/// <param name="MaxSide">The longest side a compressed image is reduced to.</param>
/// <param name="Quality">The JPEG or WebP quality of a compressed image.</param>
/// <param name="AllowOriginals">Images may go uncompressed; otherwise one larger than <paramref name="MaxSide"/> is refused.</param>
public sealed record ImageCompressionPolicy(int MaxSide, int Quality, bool AllowOriginals)
{
    public static ImageCompressionPolicy Default { get; } = new(2560, 85, true);

    /// <summary>
    /// The policy the coordinator sent. A server without these settings sends zeros, and takes any image.
    /// </summary>
    public static ImageCompressionPolicy From(int maxSide, int quality, bool allowOriginals) =>
        maxSide <= 0 ? Default : new ImageCompressionPolicy(maxSide, quality <= 0 ? Default.Quality : quality, allowOriginals);
}

/// <summary>
/// What is done to images attached from now on, as the user chose it.
/// </summary>
public sealed record ChatAttachmentPreferences(bool CompressImages = true, bool StripMetadata = true);
