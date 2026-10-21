using System.Net;
using System.Net.Sockets;

namespace Client.Tools.Parsing;

/// <summary>
/// The scheme typed in front of a server address, if any.
/// </summary>
public enum AddressScheme
{
    None,
    Secure,
    Plain
}

/// <summary>
/// A server address taken apart. <see cref="Host"/> is ready to be put into a URI: an IPv6 address
/// is in brackets. <see cref="Port"/> is null when the address has none.
/// </summary>
public sealed record ServerAddress(string Host, int? Port, AddressScheme Scheme);

/// <summary>
/// Why a typed server address or port is not accepted. The interface words it for the user.
/// </summary>
public enum ServerAddressError
{
    Empty,
    UnsupportedScheme,
    ContainsUserName,
    ContainsSpaces,
    Ipv6BracketNotClosed,
    Ipv6TrailingCharacters,
    InvalidIpv6,
    Ipv6PortWithoutBrackets,
    InvalidIpv4,
    InvalidHostName,
    PortEmpty,
    PortOutOfRange
}

public static class UriParsing
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    /// <summary>
    /// Reads what a user typed or pasted as a server address: a host name, an IPv4 or IPv6 address,
    /// optionally with a port, a ws/wss/http/https scheme and a path, which is ignored.
    /// </summary>
    /// <returns>The address, or the reason it is not one.</returns>
    public static (ServerAddress? Address, ServerAddressError? Error) ParseServerAddress(string? input)
    {
        var text = input?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return (null, ServerAddressError.Empty);

        var scheme = AddressScheme.None;
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);

        if (schemeEnd >= 0)
        {
            scheme = text[..schemeEnd].ToLowerInvariant() switch
            {
                "wss" or "https" => AddressScheme.Secure,
                "ws" or "http" => AddressScheme.Plain,
                _ => AddressScheme.None
            };

            if (scheme == AddressScheme.None)
                return (null, ServerAddressError.UnsupportedScheme);

            text = text[(schemeEnd + 3)..];
        }

        // A pasted link may carry a path, a query or a fragment; the server is only the authority part
        var authorityEnd = text.IndexOfAny(['/', '?', '#']);
        if (authorityEnd >= 0)
            text = text[..authorityEnd];

        if (text.Contains('@'))
            return (null, ServerAddressError.ContainsUserName);
        if (text.Any(char.IsWhiteSpace))
            return (null, ServerAddressError.ContainsSpaces);
        if (text.Length == 0)
            return (null, ServerAddressError.Empty);

        string host;
        string? portText = null;

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0)
                return (null, ServerAddressError.Ipv6BracketNotClosed);

            host = text[1..close];
            var rest = text[(close + 1)..];

            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                    return (null, ServerAddressError.Ipv6TrailingCharacters);
                portText = rest[1..];
            }

            if (!IPAddress.TryParse(host, out var ipv6) || ipv6.AddressFamily != AddressFamily.InterNetworkV6)
                return (null, ServerAddressError.InvalidIpv6);
        }
        else if (text.Count(c => c == ':') > 1)
        {
            // Several colons without brackets can only be an IPv6 address on its own, with no port
            if (!IPAddress.TryParse(text, out var ipv6) || ipv6.AddressFamily != AddressFamily.InterNetworkV6)
                return (null, ServerAddressError.Ipv6PortWithoutBrackets);

            host = text;
        }
        else
        {
            var colon = text.IndexOf(':');
            host = colon < 0 ? text : text[..colon];
            portText = colon < 0 ? null : text[(colon + 1)..];

            var hostError = ValidateHostName(host);
            if (hostError != null)
                return (null, hostError);
        }

        int? port = null;
        if (portText != null)
        {
            var portError = ValidatePort(portText, out var parsedPort);
            if (portError != null)
                return (null, portError);
            port = parsedPort;
        }

        return (new ServerAddress(FormatHost(host), port, scheme), null);
    }

    /// <summary>
    /// Checks a port typed on its own.
    /// </summary>
    /// <returns>Null when <paramref name="input"/> is a valid port.</returns>
    public static ServerAddressError? ValidatePort(string? input, out int port)
    {
        port = 0;
        var text = input?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return ServerAddressError.PortEmpty;
        if (!text.All(char.IsAsciiDigit) || !int.TryParse(text, out port) || port is < MinPort or > MaxPort)
            return ServerAddressError.PortOutOfRange;

        return null;
    }

    /// <summary>
    /// Puts an IPv6 address in brackets, as a URI needs it, and lowercases a host name.
    /// </summary>
    public static string FormatHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.');

        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            trimmed = trimmed[1..^1];

        if (IPAddress.TryParse(trimmed, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
            return $"[{ip}]";

        return trimmed.ToLowerInvariant();
    }

    private static ServerAddressError? ValidateHostName(string host)
    {
        var trimmed = host.TrimEnd('.');

        if (trimmed.Length == 0)
            return ServerAddressError.Empty;

        // IPAddress.TryParse takes "10" or "10.1" as shortened IPv4 addresses, which nobody means
        if (trimmed.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            var parts = trimmed.Split('.');
            var isIpv4 = parts.Length == 4
                         && parts.All(part => part.Length is > 0 and <= 3 && int.TryParse(part, out var value) && value <= 255);

            return isIpv4 ? null : ServerAddressError.InvalidIpv4;
        }

        // Single-label names such as a machine on the local network are valid too
        return Uri.CheckHostName(trimmed) == UriHostNameType.Dns ? null : ServerAddressError.InvalidHostName;
    }
}
