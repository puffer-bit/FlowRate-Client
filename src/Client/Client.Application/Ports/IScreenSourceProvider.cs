using Client.Core.Media;

namespace Client.Application.Ports;

public interface IScreenSourceProvider
{
    IReadOnlyList<ScreenSource> GetWindows();
    IReadOnlyList<ScreenSource> GetMonitors();
}
