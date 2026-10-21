using System.Collections.Generic;
using System.Linq;
using Client.Core.Media;
using Client.Application.Ports;

namespace Client.Services.ScreenCast.Windows;

public class WindowsScreenSourceProvider : IScreenSourceProvider
{
    public IReadOnlyList<ScreenSource> GetWindows() =>
        NativeWindowHelper.GetOpenWindows().Select(w => new ScreenSource(w.Title, w.Handle, w.ProcessId)).ToList();

    public IReadOnlyList<ScreenSource> GetMonitors() =>
        NativeWindowHelper.GetMonitors()
            .Select(m => new ScreenSource($"{m.DeviceName} {m.Width}×{m.Height}", m.Handle))
            .ToList();
}
