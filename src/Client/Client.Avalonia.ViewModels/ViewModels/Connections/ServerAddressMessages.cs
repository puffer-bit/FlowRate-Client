using Client.Avalonia.Localization;
using Client.Tools.Parsing;

namespace Client.ViewModels.Connections;

/// <summary>
/// What the connect window tells about an address or a port that <see cref="UriParsing"/> did not accept.
/// </summary>
public static class ServerAddressMessages
{
    public static string? Get(ServerAddressError? error) => error switch
    {
        null => null,
        ServerAddressError.Empty => Strings.Get("connect.address.empty"),
        ServerAddressError.UnsupportedScheme => Strings.Get("connect.address.unsupportedScheme"),
        ServerAddressError.ContainsUserName => Strings.Get("connect.address.containsUserName"),
        ServerAddressError.ContainsSpaces => Strings.Get("connect.address.containsSpaces"),
        ServerAddressError.Ipv6BracketNotClosed => Strings.Get("connect.address.ipv6BracketNotClosed"),
        ServerAddressError.Ipv6TrailingCharacters => Strings.Get("connect.address.ipv6TrailingCharacters"),
        ServerAddressError.InvalidIpv6 => Strings.Get("connect.address.invalidIpv6"),
        ServerAddressError.Ipv6PortWithoutBrackets => Strings.Get("connect.address.ipv6PortWithoutBrackets"),
        ServerAddressError.InvalidIpv4 => Strings.Get("connect.address.invalidIpv4"),
        ServerAddressError.InvalidHostName => Strings.Get("connect.address.invalidHostName"),
        ServerAddressError.PortEmpty => Strings.Get("connect.port.empty"),
        _ => Strings.Format("connect.port.outOfRange", ("min", UriParsing.MinPort), ("max", UriParsing.MaxPort))
    };
}
