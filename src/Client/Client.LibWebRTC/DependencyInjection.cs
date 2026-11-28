using Client.Application.Ports;
using Client.LibWebRTC.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Client.LibWebRTC;

public static class DependencyInjection
{
    /// <summary>
    /// The transports of the desktop over libwebrtc, the only engine of the sessions. Needs the
    /// <see cref="IVideoMedia"/> and <see cref="IAudioMedia"/> of the host.
    /// </summary>
    public static IServiceCollection AddLibWebRTC(this IServiceCollection services)
    {
        services.AddSingleton<LibWebRTCRuntime>();
        services.AddSingleton<ILibWebRTCLoader>(provider => provider.GetRequiredService<LibWebRTCRuntime>());
        services.AddSingleton<NativeVideoReceiver>();
        services.AddSingleton<INativeLog, LibWebRTCNativeLog>();
        services.AddSingleton<LibWebRTCTransportFactory>();
        services.AddSingleton<IWebRTCTransportFactory>(provider => provider.GetRequiredService<LibWebRTCTransportFactory>());

        return services;
    }
}
