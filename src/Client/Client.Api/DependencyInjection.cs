using Client.Api.Protocol;
using Client.Api.Transport;
using Client.Api.Transport.HTTP;
using Microsoft.Extensions.DependencyInjection;
using Client.Application;
using Client.Application.Ports;

namespace Client.Api;

public static class DependencyInjection
{
    /// <param name="client">What the client tells the server about itself when it logs in.</param>
    public static IServiceCollection AddApi(this IServiceCollection services, ClientInfo client)
    {
        services.AddSingleton(client);
        services.AddTransient<ICoordinatorGateway, CoordinatorGateway>();
        services.AddTransient<IMessageParser, MessageParser>();
        services.AddTransient<IHeartbeatHandler, HeartbeatHandler>();
        services.AddTransient<ResponseAwaiter>();
        services.AddTransient<IWebSocketGateway, WebSocketGateway>();
        services.AddTransient<IPushReplyTransport, PushReplyHttpTransport>();

        return services;
    }
}
