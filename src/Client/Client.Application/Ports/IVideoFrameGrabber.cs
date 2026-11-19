namespace Client.Application.Ports;

/// <summary>
/// A frame of a video as it is stored, in BGRA rows without padding.
/// </summary>
/// <param name="Rotation">How far it is turned clockwise to be shown: 0, 90, 180 or 270 degrees.</param>
public sealed record VideoFrameImage(byte[] Bgra, int Width, int Height, int Rotation);

/// <summary>Takes a frame of a video file to show it by before it is played.</summary>
public interface IVideoFrameGrabber
{
    /// <summary>The media runtime is loaded and can decode videos.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A frame from the start of the video, a second in or earlier for a shorter one, not larger than
    /// <paramref name="maxSide"/> on its longer side.
    /// </summary>
    /// <returns>Null when the file is not a video this can decode.</returns>
    Task<VideoFrameImage?> GrabAsync(string path, int maxSide, CancellationToken cancellationToken = default);
}
