using Client.Application.Ports;
using Client.LibWebRTC.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Client.LibWebRTC;

public static class DependencyInjection
{
    /// <summary>
    /// The transports of the desktop over libwebrtc. Registered beside the ones of GStreamer, not over
    /// them: the host picks the engine of each session. Needs the <see cref="IVideoMedia"/> of the host.
    /// </summary>
    public static IServiceCollection AddLibWebRTC(this IServiceCollection services)
    {
        services.AddSingleton<LibWebRTCRuntime>();
        services.AddSingleton<ILibWebRTCLoader>(provider => provider.GetRequiredService<LibWebRTCRuntime>());
        services.AddSingleton<NativeVideoReceiver>();
        services.AddSingleton<LibWebRTCTransportFactory>();

        return services;
    }
}
