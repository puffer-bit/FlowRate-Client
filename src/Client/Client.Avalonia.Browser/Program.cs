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

        // Host-only mode: browser capabilities are independent of desktop launch overrides.
        Client.Core.ApplicationRuntime.Configure(["--browser"]);
        MediaRuntime.Configure([]);
        // Browser .NET has neither ECDsa nor AesGcm: BouncyCastle stands in for both
        Client.Application.Security.EcdsaKeys.Factory = () => new Platform.BouncyCastleECDsa();
        Client.Application.Security.AesGcmCiphers.Factory = (key, tagLength) => new Platform.BouncyCastleAesGcm(key, tagLength);

        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .WithAppFonts()
            .UseReactiveUI(_ => { });
}
