using Client.Api.Protocol;
using Client.Api.Transport;
using Client.Api.Transport.HTTP;
using Microsoft.Extensions.DependencyInjection;
using Client.Application;
using Client.Application.Ports;

namespace Client.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddTransient<ICoordinatorGateway, CoordinatorGateway>();
        services.AddTransient<IMessageParser, MessageParser>();
        services.AddTransient<IHeartbeatHandler, HeartbeatHandler>();
        services.AddTransient<ResponseAwaiter>();
        services.AddTransient<IWebSocketGateway, WebSocketGateway>();
        services.AddTransient<IPushReplyTransport, PushReplyHttpTransport>();

        return services;
    }
}
