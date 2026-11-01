using Client.Application.Ports;
using Client.Droid.Platform.Firebase;
using Client.Droid.Platform.Media;
using Client.Droid.Platform.Media.WebRTC;
using Client.Droid.Platform.Network;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Droid.Platform;

internal static class DependencyInjection
{
    /// <summary>
    /// What the desktop gets from Client.GStreamer and Client.OpenAL, as far as loading the application
    /// and the settings of the sounds need it. The ports of voice, screen sharing and the settings dialog
    /// are not registered: nothing on Android opens them yet. Voice goes over libwebrtc, see
    /// <see cref="Media.WebRTC.AndroidVoicePeer"/>.
    /// </summary>
    public static IServiceCollection AddAndroidPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IMediaRuntimeLoader, NoMediaRuntimeLoader>();
        services.AddSingleton<IAudioPlayerService, SilentAudioPlayerService>();
        services.AddSingleton<ISoundPlayer, AndroidSoundPlayer>();
        services.AddSingleton<IAudioDeviceProvider, NoAudioDeviceProvider>();
        services.AddSingleton<IMicrophoneTester, NoMicrophoneTester>();
        services.AddSingleton<IWebRTCTransportFactory, AndroidWebRTCTransportFactory>();
        services.AddSingleton<ILibWebRTCLoader, AndroidLibWebRTCLoader>();
        services.AddSingleton<INativeLog, AndroidWebRTCNativeLog>();
        services.AddSingleton<IMediaCapabilities, AndroidMediaCapabilities>();
        services.AddSingleton<IPushTokenProvider, FirebasePushTokenProvider>();

        // Replaces the one of the runtime, which is not told about the networks of the device here
        services.AddSingleton<IConnectivityMonitor, AndroidConnectivityMonitor>();

        return services;
    }
}
