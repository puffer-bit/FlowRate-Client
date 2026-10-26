using System;
using Android;
using Android.Content.PM;
using AndroidX.Core.Content;
using Client.Application.Ports;
using Client.LibWebRTC.Voice;
using Microsoft.Extensions.Logging;

namespace Client.Droid.Platform.Media.WebRTC;

internal sealed class AndroidWebRTCTransportFactory(ILoggerFactory loggerFactory) : IWebRTCTransportFactory
{
    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true) =>
        new VoiceTransport(sessionId, listener => new VoicePeerWorker(async peerListener =>
        {
            // The peer must not start recording while the permission dialog is still open.
            var context = global::Android.App.Application.Context;
            if (ContextCompat.CheckSelfPermission(context, Manifest.Permission.RecordAudio) != Permission.Granted &&
                (MainActivity.Current is not { } activity || !await activity.RequestMicrophonePermissionAsync()))
                throw new InvalidOperationException("Microphone permission was not granted.");

            return new AndroidVoicePeer(peerListener);
        }, listener), loggerFactory.CreateLogger<VoiceTransport>(), isPlaybackEnabled);
}
