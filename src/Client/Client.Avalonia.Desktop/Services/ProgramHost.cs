using Client.Views.Common;

namespace Client.Services;

/// <summary>Restarts the application for the desktop views, see <see cref="Program"/>.</summary>
internal sealed class ProgramHost : IDesktopHost
{
    public void Restart() => Program.Restart();

    public void RestartWithoutMedia() => Program.RestartWithoutMedia();
}
