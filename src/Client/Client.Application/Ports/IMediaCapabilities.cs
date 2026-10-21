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

    /// <summary>
    /// The encoders of shared screens this system has, in the order they are preferred.
    /// </summary>
    IReadOnlyList<string> ScreenShareEncoders { get; }

    /// <summary>
    /// Whether the sound of a shared screen can be captured: of a single application, or of the
    /// whole system without our own sound.
    /// </summary>
    bool CanCaptureScreenAudio { get; }
}
