using System.Threading;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.Tools.Logging;
using Com.Flowrate.Rtc;
using Microsoft.Extensions.Logging;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// Passes the log of libwebrtc into the journal, under <see cref="ClientLogging.LibWebRTCNativeCategory"/>.
/// libwebrtc of Android takes its logger only when it starts, see <c>VoicePeer.setLog</c>: a level asked
/// for later is applied as far as the one it started with allows, the rest after a restart.
/// </summary>
internal sealed partial class AndroidWebRTCNativeLog : INativeLog
{
    // The order of Logging.Severity
    private const int SeverityVerbose = 0;
    private const int SeverityInfo = 1;
    private const int SeverityWarning = 2;
    private const int SeverityError = 3;
    private const int SeverityNone = 4;

    private readonly ILogger<AndroidWebRTCNativeLog> _logger;
    private readonly Listener _listener;

    public AndroidWebRTCNativeLog(ILoggerFactory loggerFactory, ILogger<AndroidWebRTCNativeLog> logger)
    {
        _logger = logger;
        _listener = new Listener(loggerFactory.CreateLogger(ClientLogging.LibWebRTCNativeCategory));
    }

    public NativeLogSource Source => NativeLogSource.LibWebRTC;

    public void SetLevel(NativeLogLevel level)
    {
        var severity = level switch
        {
            NativeLogLevel.Off => SeverityNone,
            NativeLogLevel.Error => SeverityError,
            NativeLogLevel.Warning => SeverityWarning,
            NativeLogLevel.Verbose => SeverityVerbose,
            _ => SeverityInfo
        };

        _listener.MinSeverity = severity;

        if (!VoicePeer.SetLog(_listener, severity))
            LogAfterRestart(level);
    }

    [LoggerMessage(LogLevel.Information,
        "libwebrtc has started already: its log at {Level} is passed from the next start of the application")]
    private partial void LogAfterRestart(NativeLogLevel level);

    private sealed partial class Listener(ILogger native) : Java.Lang.Object, VoicePeer.ILogListener
    {
        private int _minSeverity = SeverityNone;

        // Messages below it are dropped here: libwebrtc keeps the level it started with
        public int MinSeverity
        {
            get => Volatile.Read(ref _minSeverity);
            set => Volatile.Write(ref _minSeverity, value);
        }

        public void OnLog(int severity, string? tag, string? message)
        {
            if (severity < MinSeverity)
                return;

            var level = severity switch
            {
                SeverityError => LogLevel.Error,
                SeverityWarning => LogLevel.Warning,
                SeverityInfo => LogLevel.Information,
                _ => LogLevel.Trace
            };

            LogMessage(native, level, tag, message?.TrimEnd());
        }

        [LoggerMessage(Message = "{Tag}: {Text}")]
        private static partial void LogMessage(ILogger logger, LogLevel level, string? tag, string? text);
    }
}
