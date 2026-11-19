using Client.Application.Chat;
using Client.Core.Chat;

namespace Client.Application.Ports;

/// <summary>What is done to an image before it is sent.</summary>
/// <param name="Compress">Reduced to the side of the policy and encoded again with its quality.</param>
/// <param name="StripMetadata">Without EXIF, XMP and text: where and when it was taken, the camera.</param>
public sealed record ImagePreparation(bool Compress, bool StripMetadata, ImageCompressionPolicy Policy);

/// <summary>
/// An image as it will be sent.
/// </summary>
/// <param name="Content">The bytes to send, or null to send the file as it is.</param>
/// <param name="FileName">The name it goes under, with the extension of its format.</param>
/// <param name="Width">Of the original, as it is shown.</param>
/// <param name="CanCompress">Not animated: compressing would keep only its first frame.</param>
public sealed record PreparedImage(byte[]? Content, string FileName, int Width, int Height, bool CanCompress)
{
    public int LongerSide => Math.Max(Width, Height);
}

public interface IImagePreparer
{
    /// <returns>Null when the content is not an image this can read.</returns>
    Task<PreparedImage?> PrepareAsync(ChatMediaSource source, ImagePreparation preparation,
        CancellationToken cancellationToken = default);
}
