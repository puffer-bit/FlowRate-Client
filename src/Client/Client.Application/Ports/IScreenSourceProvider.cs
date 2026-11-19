using Client.Core.Media;

namespace Client.Application.Ports;

public interface IScreenSourceProvider
{
    /// <summary>
    /// Whether the system lets the application capture the screen. Where it asks the user, it is
    /// asked here; the answer may take effect only after a restart.
    /// </summary>
    bool RequestAccess() => true;

    IReadOnlyList<ScreenSource> GetWindows();
    IReadOnlyList<ScreenSource> GetMonitors();
}
