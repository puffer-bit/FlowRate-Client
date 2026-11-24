using System.Runtime.InteropServices.JavaScript;
using Client.Views.Common;

namespace Client.Browser.Platform;

/// <summary>Restarting the application in the browser is loading the page again.</summary>
internal sealed partial class BrowserHost : IDesktopHost
{
    public void Restart() => Reload();

    public void RestartWithoutMedia() => Reload();

    [JSImport("globalThis.location.reload")]
    private static partial void Reload();
}
