using System.Net;

namespace Client.Api.Transport;

/// <summary>
/// The route every HTTP request to a coordinator takes: the one its web socket takes. The system proxy
/// is asked about the wss address, not the https one, so the media of a coordinator are reached the way
/// its messages are. On macOS a system proxy covers https but not wss: without this a coordinator on the
/// local network keeps its messages and loses its images to a proxy that cannot reach it.
/// </summary>
internal sealed class WebSocketRouteProxy : IWebProxy
{
    public static WebSocketRouteProxy Instance { get; } = new();

    private WebSocketRouteProxy()
    {
    }

    public ICredentials? Credentials
    {
        get => HttpClient.DefaultProxy.Credentials;
        set => HttpClient.DefaultProxy.Credentials = value;
    }

    public Uri? GetProxy(Uri destination) => HttpClient.DefaultProxy.GetProxy(AsWebSocket(destination));

    public bool IsBypassed(Uri host) => HttpClient.DefaultProxy.IsBypassed(AsWebSocket(host));

    private static Uri AsWebSocket(Uri destination)
    {
        var scheme = destination.Scheme == Uri.UriSchemeHttp ? Uri.UriSchemeWs
            : destination.Scheme == Uri.UriSchemeHttps ? Uri.UriSchemeWss
            : null;
        return scheme == null ? destination : new UriBuilder(destination) { Scheme = scheme }.Uri;
    }
}
