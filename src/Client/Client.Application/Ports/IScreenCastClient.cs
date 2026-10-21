namespace Client.Application.Ports;

/// <summary>
/// Screen sharing through the system: the system shows its own dialog and decides what the
/// application may capture. Available where the application cannot enumerate the sources itself.
/// </summary>
public interface IScreenCastClient : IDisposable
{
    /// <summary>
    /// Raised when the system ends the capture by itself, for example from its sharing indicator.
    /// Raised on any thread, and not for <see cref="CloseAsync"/>.
    /// </summary>
    event Action? Closed;

    /// <summary>
    /// Lets the user pick a screen or a window. An earlier selection is closed first.
    /// </summary>
    /// <returns>The source to capture, or null when the user closed the dialog.</returns>
    /// <exception cref="ScreenCastException">The system refused or the portal is not available.</exception>
    Task<VideoInput?> SelectSourceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the capture of the selected source. The system stops showing its sharing indicator.
    /// </summary>
    Task CloseAsync();
}

public enum ScreenCastError
{
    /// <summary>The desktop has no screen cast portal, or it did not answer.</summary>
    PortalUnavailable,

    /// <summary>The portal answered with an error or with something unexpected.</summary>
    PortalFailed
}

public sealed class ScreenCastException(ScreenCastError error, string details, Exception? inner = null)
    : Exception(details, inner)
{
    public ScreenCastError Error { get; } = error;
}
