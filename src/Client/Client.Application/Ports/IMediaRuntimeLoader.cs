namespace Client.Application.Ports;

public interface IMediaRuntimeLoader
{
    bool Load();

    /// <summary>
    /// Whether the media components come with the application and not from the system, so that
    /// reinstalling the application restores them when <see cref="Load"/> fails.
    /// </summary>
    bool UsesBundledRuntime { get; }
}
