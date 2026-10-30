using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.GStreamer.Interop;
using Client.Tools.Logging;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Loader;

/// <summary>
/// Passes the debug log of GStreamer (what GST_DEBUG would print) into the journal, under
/// <see cref="ClientLogging.GStreamerNativeCategory"/>, as much of it as the level says. A GST_DEBUG set
/// in the environment is left alone while the level is off, so that it still works for debugging.
/// </summary>
public sealed unsafe partial class GstNativeLog(ILoggerFactory loggerFactory) : INativeLog
{
    private const int LevelNone = 0;
    private const int LevelError = 1;
    private const int LevelWarning = 2;
    private const int LevelFixme = 3;
    private const int LevelInfo = 4;
    private const int LevelDebug = 5;
    private const int LevelLog = 6;

    private static readonly Lock Gate = new();
    private static ILogger? _logger;
    private static NativeLogLevel? _level;
    private static bool _isLoaded;
    private static bool _isPassing;

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_debug_set_default_threshold(int level);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_debug_add_log_function(
        delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr, void> func,
        IntPtr userData, IntPtr notify);

    // NULL removes the default function, the one that prints to stderr
    [DllImport(NativeLibraries.GStreamer)]
    private static extern uint gst_debug_remove_log_function(IntPtr func);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_debug_category_get_name(IntPtr category);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_debug_message_get(IntPtr message);

    public NativeLogSource Source => NativeLogSource.GStreamer;

    public void SetLevel(NativeLogLevel level)
    {
        lock (Gate)
        {
            _logger ??= loggerFactory.CreateLogger(ClientLogging.GStreamerNativeCategory);
            _level = level;

            if (_isLoaded)
                Apply();
        }
    }

    /// <summary>
    /// Called by <see cref="GstLoader"/> once GStreamer is initialized: a level set before takes effect now.
    /// </summary>
    internal static void OnLoaded()
    {
        lock (Gate)
        {
            _isLoaded = true;

            if (_level != null)
                Apply();
        }
    }

    private static void Apply()
    {
        var hasEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GST_DEBUG"));

        if (_level is not { } level || level == NativeLogLevel.Off)
        {
            if (!hasEnvironment)
                gst_debug_set_default_threshold(LevelNone);
            return;
        }

        if (!_isPassing)
        {
            gst_debug_add_log_function(&OnMessage, IntPtr.Zero, IntPtr.Zero);

            // Everything now goes to the journal, which prints it to the console as well
            gst_debug_remove_log_function(IntPtr.Zero);
            _isPassing = true;
        }

        gst_debug_set_default_threshold(level switch
        {
            NativeLogLevel.Error => LevelError,
            NativeLogLevel.Warning => LevelWarning,
            NativeLogLevel.Info => LevelInfo,
            NativeLogLevel.Debug => LevelDebug,
            _ => LevelLog
        });
    }

    // GstLogFunction; comes on any thread of GStreamer, often a streaming one
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMessage(IntPtr category, int level, IntPtr file, IntPtr function, int line,
        IntPtr gobject, IntPtr message, IntPtr userData)
    {
        try
        {
            if (_logger is not { } logger)
                return;

            var logLevel = level switch
            {
                LevelError => LogLevel.Error,
                LevelWarning or LevelFixme => LogLevel.Warning,
                LevelInfo => LogLevel.Information,
                LevelDebug => LogLevel.Debug,
                _ => LogLevel.Trace
            };

            if (!logger.IsEnabled(logLevel))
                return;

            var name = Marshal.PtrToStringUTF8(gst_debug_category_get_name(category));
            var text = Marshal.PtrToStringUTF8(gst_debug_message_get(message));
            var fileName = Path.GetFileName(Marshal.PtrToStringUTF8(file));

            LogMessage(logger, logLevel, name, fileName, line, text);
        }
        catch
        {
            // Nothing may be thrown into GStreamer
        }
    }

    [LoggerMessage(Message = "{GstCategory} {File}:{Line}: {Text}")]
    private static partial void LogMessage(ILogger logger, LogLevel level, string? gstCategory, string? file,
        int line, string? text);
}
