using System;
using Environment = System.Environment;
using System.IO;
using System.Threading;
using Android.OS;
using Client.Tools.Logging;
using Serilog;

namespace Client.Droid.Platform;

/// <summary>
/// The journal of the process. The system starts it for the interface or for a push alone, so it is set
/// up by the application object, before either of them.
/// </summary>
internal static class AndroidLogging
{
    private static int _isStarted;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
            return;

        Log.Logger = ClientLogging.CreateSerilogLogger(new LoggingOptions
        {
            WriteToFiles = true,
            // Next to the database; the files folder of the application is only its own
            LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logs"),
            // The phone has less room than a desktop
            FileSizeLimitBytes = 10 * 1024 * 1024,
            RetainedFileCount = 5,
            // The console ends up in logcat, which shows no colors
            UseConsoleColors = false
        });
        ClientLogging.LogUnhandledExceptions();
        ClientLogging.LogStartup(ApplicationDetails.Version,
            $"{Build.Manufacturer} {Build.Model}, {ApplicationDetails.SystemDescription}");
    }
}
