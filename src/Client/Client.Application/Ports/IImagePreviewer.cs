namespace Client.Application.Ports;

/// <summary>
/// What a message needs to know about an image before it is sent.
/// </summary>
/// <param name="MimeType">The format the file really is, whatever its name says.</param>
/// <param name="Width">As the image is shown, that is after the rotation its metadata asks for.</param>
/// <param name="Preview">The reduced copy, already rotated.</param>
public record ImagePreview(string MimeType, int Width, int Height, byte[] Preview, string PreviewMimeType);

public interface IImagePreviewer
{
    /// <returns>Null when the content is not an image the chat can carry.</returns>
    Task<ImagePreview?> CreateAsync(Func<Task<Stream>> openContent, CancellationToken cancellationToken = default);
}
