using System.Runtime.InteropServices;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.Tools.Logging;
using Microsoft.Extensions.Logging;

namespace Client.LibWebRTC.Native;

/// <summary>
/// Passes the log of libwebrtc into the journal, under <see cref="ClientLogging.LibWebRTCNativeCategory"/>,
/// through fr_set_log. libwebrtc knows four levels: Info and Debug both pass its info messages.
/// </summary>
public sealed unsafe partial class LibWebRTCNativeLog(ILoggerFactory loggerFactory, ILogger<LibWebRTCNativeLog> logger)
    : INativeLog
{
    // The LoggingSeverity of WebRTC
    private const int SeverityVerbose = 0;
    private const int SeverityInfo = 1;
    private const int SeverityWarning = 2;
    private const int SeverityError = 3;
    private const int SeverityNone = 4;

    private static ILogger? _native;

    private readonly Lock _lock = new();
    private bool _isPassing;

    public NativeLogSource Source => NativeLogSource.LibWebRTC;

    public void SetLevel(NativeLogLevel level)
    {
        lock (_lock)
        {
            // Off and never on: the library is not even loaded for it
            if (level == NativeLogLevel.Off && !_isPassing)
                return;

            _native ??= loggerFactory.CreateLogger(ClientLogging.LibWebRTCNativeCategory);

            var severity = level switch
            {
                NativeLogLevel.Off => SeverityNone,
                NativeLogLevel.Error => SeverityError,
                NativeLogLevel.Warning => SeverityWarning,
                NativeLogLevel.Verbose => SeverityVerbose,
                _ => SeverityInfo
            };

            try
            {
                NativeMethods.SetLog(severity == SeverityNone ? null : &OnMessage, severity);
                _isPassing = severity != SeverityNone;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                                  or BadImageFormatException)
            {
                // No library, or one built before fr_set_log
                LogUnavailable(exception.Message);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static void OnMessage(int severity, byte* message)
    {
        try
        {
            if (_native is not { } native)
                return;

            var level = severity switch
            {
                SeverityError => LogLevel.Error,
                SeverityWarning => LogLevel.Warning,
                SeverityInfo => LogLevel.Information,
                _ => LogLevel.Trace
            };

            if (native.IsEnabled(level))
                LogMessage(native, level, Marshal.PtrToStringUTF8((nint)message));
        }
        catch
        {
            // Nothing may be thrown into libwebrtc
        }
    }

    [LoggerMessage(Message = "{Text}")]
    private static partial void LogMessage(ILogger logger, LogLevel level, string? text);

    [LoggerMessage(LogLevel.Warning, "The log of libwebrtc cannot be passed to the journal: {Error}")]
    private partial void LogUnavailable(string error);
}
