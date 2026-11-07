using Avalonia;
using System;
using Client.Core.Media;
using Client.Avalonia.Icons;
using Client.GStreamer.Loader;
using Client.Tools.AppInfo;
using Client.Services.Logging;
using Client.Tools.Logging;
using ReactiveUI.Avalonia;
using Serilog;
using Velopack;

namespace Client;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // First of all: the installer and the updater start the application to run its hooks, and it
        // exits right after them. A downloaded update the user did not restart into is applied here too
        VelopackApp.Build().Run();

        try
        {
            LaunchOptions.Configure(args);
            if (LaunchOptions.Current.Help || LaunchOptions.Current.Version)
            {
                Client.Services.LaunchConsole.Attach();
                Console.WriteLine(LaunchOptions.Current.Help ? LaunchOptions.HelpText : AppInformation.GetClientBuildVersion());
                return;
            }
            AppDirectories.ConfigureFromArguments(args);
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or UnauthorizedAccessException
                                  or NotSupportedException or System.Security.SecurityException)
        {
            Client.Services.LaunchConsole.Attach();
            Console.Error.WriteLine($"Cannot start FlowRate: {e.Message}");
            Environment.ExitCode = 1;
            return;
        }

        // Before Avalonia, so that a crash at any point of the start is written to the log files too
        Log.Logger = ClientLogging.CreateSerilogLogger(new LoggingOptions
        {
            WriteToFiles = !LaunchOptions.Current.NoFileLogging,
            MinimumLevel = LaunchOptions.Current.LogLevel ?? new LoggingOptions().MinimumLevel
        });
        ClientLogging.LogUnhandledExceptions();
        ClientLogging.LogStartup(AppInformation.GetClientBuildVersion());

        try
        {
            MediaRuntime.Configure(LaunchOptions.Current.DisableMedia ? ["--no-media"] : []);
            if (!MediaRuntime.IsEnabled)
            {
                Log.Information("Media disabled by launch options; OpenAL interface sounds remain available");
            }
            if (MediaRuntime.IsEnabled)
                GstNativeLibraryResolver.Register();
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            Log.Fatal(e, "The application crashed");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .WithEmojiFont()
            .LogToJournal()
            // The main window draws its own title bar; on X11 that needs the frame drawn by Avalonia too.
            // Avalonia 12.1 marks the option experimental
#pragma warning disable AVALONIA_X11_CSD
            .With(new X11PlatformOptions
            {
                EnableDrawnDecorations = true,
                // EGL first: only an EGL context imports the DMA-BUF frames of hardware decoders, see
                // VideoSurfaceProbe. GLX and the framebuffer remain where EGL does not start
                RenderingMode = LaunchOptions.Current.SoftwareRendering ? [X11RenderingMode.Software]
                    : [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software]
            })
#pragma warning restore AVALONIA_X11_CSD
            // ANGLE on Direct3D 11 is the only Windows renderer that shares GL contexts and imports
            // D3D11 frames, see VideoSurfaceProbe
            .With(new Win32PlatformOptions
            {
                RenderingMode = LaunchOptions.Current.SoftwareRendering ? [Win32RenderingMode.Software]
                    : [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            })
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = 256 * 1024 * 1024 })
            .UseSkia()
            .UseReactiveUI(_ => { })
#if DEBUG
            .WithDeveloperTools()
#endif
            ;
}
