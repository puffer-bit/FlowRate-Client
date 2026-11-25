using Client.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Browser.Platform;

internal static class DependencyInjection
{
    public static IServiceCollection AddBrowserPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IMediaRuntimeLoader, NoMediaRuntimeLoader>();
        services.AddSingleton<ILibWebRTCLoader, NoLibWebRTCLoader>();
        services.AddSingleton<IWebRTCTransportFactory, NoWebRTCTransportFactory>();
        services.AddSingleton<IMediaCapabilities, NoMediaCapabilities>();
        services.AddSingleton<IAudioDeviceProvider, NoAudioDeviceProvider>();
        services.AddSingleton<IAudioPlayerService, SilentAudioPlayerService>();
        services.AddSingleton<ISoundPlayer, SilentSoundPlayer>();
        services.AddSingleton<IMicrophoneTester, NoMicrophoneTester>();

        services.AddSingleton<Client.Services.ChatMediaPlayback>();
        services.AddSingleton<NoChatMediaSession>();
        services.AddSingleton<Client.Services.IChatAudioSession>(provider => provider.GetRequiredService<NoChatMediaSession>());
        services.AddSingleton<Client.Services.IChatVideoSession>(provider => provider.GetRequiredService<NoChatMediaSession>());

        // Over the one of AddApplication: NetworkChange is not in the browser
        services.AddSingleton<IConnectivityMonitor, BrowserConnectivityMonitor>();

        return services;
    }
}
