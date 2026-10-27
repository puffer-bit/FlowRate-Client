using Microsoft.Extensions.DependencyInjection;

namespace Client.GStreamer.WebRTC;

public static class WebRTCDependencyInjection
{
    /// <summary>
    /// The transports over webrtcbin. Registered beside the ones of libwebrtc, not as the transport
    /// factory: the host picks the engine of each session. Needs <c>AddGStreamer</c>.
    /// </summary>
    public static IServiceCollection AddGStreamerWebRTC(this IServiceCollection services)
    {
        services.AddSingleton<WebRTCTransportFactory>();

        return services;
    }
}
