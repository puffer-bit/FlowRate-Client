using Client.LibWebRTC.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Client.LibWebRTC;

public static class DependencyInjection
{
    /// <summary>
    /// The voice transports of the desktop over libwebrtc. Registered beside the ones of GStreamer,
    /// not over them: the host picks the engine of each session.
    /// </summary>
    public static IServiceCollection AddLibWebRTC(this IServiceCollection services)
    {
        services.AddSingleton<LibWebRTCRuntime>();
        services.AddSingleton<LibWebRTCTransportFactory>();

        return services;
    }
}
