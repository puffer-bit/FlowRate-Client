using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Client.Avalonia.Icons;
using Client.Core.Media;
using Client.Tools.Logging;
using ReactiveUI.Avalonia;
using Serilog;

namespace Client.Browser;

internal sealed class Program
{
    private static Task Main(string[] args)
    {
        // Logs go to the console of the browser; there are no log files
        Log.Logger = ClientLogging.CreateSerilogLogger(new LoggingOptions { WriteToFiles = false, UseConsoleColors = false });

        // No voice and no video in the browser: the same mode as --no-media on the desktop
        MediaRuntime.Configure(["--no-media"]);
        Client.Application.Security.EcdsaKeys.Factory = () => new Platform.BouncyCastleECDsa();

        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .WithAppFonts()
            .UseReactiveUI(_ => { });
}
