using Client.Application.Ports;

namespace Client.Droid.Platform.Media;

/// <summary>
/// There are no media components to load yet: voice and screen sharing are not on Android.
/// </summary>
internal sealed class NoMediaRuntimeLoader : IMediaRuntimeLoader
{
    public bool Load() => true;

    public bool UsesBundledRuntime => true;
}
