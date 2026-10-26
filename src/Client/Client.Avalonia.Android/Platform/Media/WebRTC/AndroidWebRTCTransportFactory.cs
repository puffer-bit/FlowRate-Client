using Android;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Client.Application.Ports;
using Client.LibWebRTC.Voice;
using Com.Flowrate.Rtc;
using Microsoft.Extensions.Logging;

namespace Client.Droid.Platform.Media.WebRTC;

internal sealed class AndroidWebRTCTransportFactory(ILoggerFactory loggerFactory) : IWebRTCTransportFactory
{
    private const int MicrophonePermissionRequest = 7001;

    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true)
    {
        var context = global::Android.App.Application.Context;

        VoicePeer.Initialize(context);

        // Asked when the session is entered; the microphone starts once the transport is up, by which
        // time the answer is usually in. Without it the others hear silence
        if (ContextCompat.CheckSelfPermission(context, Manifest.Permission.RecordAudio) != Permission.Granted &&
            MainActivity.Current is { } activity)
            ActivityCompat.RequestPermissions(activity, [Manifest.Permission.RecordAudio], MicrophonePermissionRequest);

        return new VoiceTransport(sessionId, listener => new AndroidVoicePeer(listener),
            loggerFactory.CreateLogger<VoiceTransport>(), isPlaybackEnabled);
    }
}
