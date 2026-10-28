using Android;
using Android.Content.PM;
using AndroidX.Core.Content;
using Client.Application.Ports;
using Client.Droid.Platform.Calls;
using Client.LibWebRTC.Voice;
using Microsoft.Extensions.Logging;

namespace Client.Droid.Platform.Media.WebRTC;

internal sealed partial class AndroidWebRTCTransportFactory(ILoggerFactory loggerFactory) : IWebRTCTransportFactory
{
    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true)
    {
        var logger = loggerFactory.CreateLogger<VoiceTransport>();

        return new VoiceTransport(sessionId, listener => new VoicePeerWorker(async peerListener =>
        {
            // The peer must not start recording while the permission dialog is still open.
            // Without the permission the session is still heard and its screens watched.
            var context = global::Android.App.Application.Context;
            var isMicrophoneAllowed =
                ContextCompat.CheckSelfPermission(context, Manifest.Permission.RecordAudio) == Permission.Granted ||
                (MainActivity.Current is { } activity && await activity.RequestMicrophonePermissionAsync());

            if (isMicrophoneAllowed)
                CallService.OnMicrophoneAllowed();
            else
                LogMicrophoneDenied(logger, sessionId);

            return new AndroidVoicePeer(peerListener, isMicrophoneAllowed);
        }, listener), logger, isPlaybackEnabled, isVideoDecodedByPeer: true);
    }

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Microphone permission was not granted: the session is only listened to")]
    private static partial void LogMicrophoneDenied(ILogger logger, int sessionId);
}
