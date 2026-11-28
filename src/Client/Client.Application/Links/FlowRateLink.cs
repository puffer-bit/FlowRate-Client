using System.Text;

namespace Client.Application.Links;

/// <summary>
/// What a <c>flowrate://</c> link, or the text of a scanned QR code, asks the application to do. A link never acts
/// on its own: it only fills in a form the user then confirms, because any page or message can open one.
/// </summary>
public abstract record FlowRateLink;

/// <summary>Opens the connect form on a server.</summary>
/// <param name="Port">Null for the default port of the server.</param>
/// <param name="Fingerprint">
/// SHA-256 of the certificate, in hex, that the server is expected to present when the system cannot verify it.
/// </param>
public sealed record ConnectLink(string Host, int? Port, string? Fingerprint) : FlowRateLink;

/// <summary>Opens the connect form on a server, logging in with a one-time code of the control panel.</summary>
public sealed record LoginLink(string Host, int? Port, string? Fingerprint, string Code) : FlowRateLink;

/// <summary>
/// The code a new device shows to be approved by a device of the account. <see cref="Host"/> is the server the new
/// device waits on; null for the plain codes of older versions, which belong to the server connected to.
/// </summary>
public sealed record DeviceLinkApproval(string Code, string? Host, int? Port) : FlowRateLink;

/// <summary>
/// Opens the settings of the application: flowrate://settings?tab=about. <see cref="Tab"/> names a tab as the settings
/// do, whatever the case; null or unknown opens the first one.
/// </summary>
public sealed record SettingsLink(string? Tab) : FlowRateLink;

/// <summary>Reads and writes the links of <see cref="FlowRateLink"/>.</summary>
public static class FlowRateLinks
{
    public const string Scheme = "flowrate";

    // What the new devices of earlier versions put into their QR code
    private const string LegacyDeviceLinkPrefix = "flowrate-link:";

    public static string Connect(string host, int? port, string? fingerprint) =>
        Build("connect", ("host", host), ("port", port?.ToString()), ("cert", fingerprint));

    public static string Login(string host, int? port, string? fingerprint, string code) =>
        Build("login", ("host", host), ("port", port?.ToString()), ("cert", fingerprint), ("code", code));

    public static string DeviceLink(string code, string? host, int? port) =>
        Build("link", ("code", code), ("host", host), ("port", port?.ToString()));

    public static string Settings(string? tab = null) => Build("settings", ("tab", tab));

    /// <summary>Whether the text is meant as a link of the application, well formed or not.</summary>
    public static bool LooksLikeLink(string? text) =>
        text?.Trim() is { } trimmed && (trimmed.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)
                                        || trimmed.StartsWith(LegacyDeviceLinkPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The link the text holds; null when it is not one or is not complete.</summary>
    public static FlowRateLink? Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        if (trimmed.StartsWith(LegacyDeviceLinkPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = trimmed[LegacyDeviceLinkPrefix.Length..].Trim();
            return IsCode(code) ? new DeviceLinkApproval(code, null, null) : null;
        }

        if (!trimmed.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
            return null;

        // flowrate://connect?…, and flowrate:connect?… as some systems pass it on
        var rest = trimmed[(Scheme.Length + 1)..].TrimStart('/');
        var queryStart = rest.IndexOf('?');
        var action = (queryStart < 0 ? rest : rest[..queryStart]).TrimEnd('/').ToLowerInvariant();
        var query = ReadQuery(queryStart < 0 ? "" : rest[(queryStart + 1)..]);
        if (query == null)
            return null;

        var host = Host(query.GetValueOrDefault("host"));
        if (!TryReadPort(query.GetValueOrDefault("port"), out var port))
            return null;
        var fingerprint = Fingerprint(query.GetValueOrDefault("cert"));
        var linkCode = query.GetValueOrDefault("code")?.Trim();

        return action switch
        {
            "connect" when host != null => new ConnectLink(host, port, fingerprint),
            "login" when host != null && IsCode(linkCode) => new LoginLink(host, port, fingerprint, linkCode!),
            "link" when IsCode(linkCode) => new DeviceLinkApproval(linkCode!, host, host == null ? null : port),
            "settings" => new SettingsLink(query.GetValueOrDefault("tab")?.Trim() is { Length: > 0 } tab ? tab : null),
            _ => null
        };
    }

    /// <summary>
    /// The code of a device link as typed, pasted or scanned: a plain code stays as it is, a link gives its code.
    /// </summary>
    public static string DeviceLinkCode(string text) =>
        Parse(text) is DeviceLinkApproval approval ? approval.Code : text.Trim();

    /// <summary>Whether two fingerprints are of the same certificate, whatever their case and separators.</summary>
    public static bool IsSameFingerprint(string? first, string? second) =>
        Fingerprint(first) is { } a && Fingerprint(second) is { } b && string.Equals(a, b, StringComparison.Ordinal);

    private static string Build(string action, params (string Name, string? Value)[] parameters)
    {
        var builder = new StringBuilder(Scheme).Append("://").Append(action);
        var separator = '?';
        foreach (var (name, value) in parameters)
        {
            if (string.IsNullOrEmpty(value))
                continue;
            builder.Append(separator).Append(name).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }

    // Null when a parameter is written twice: such a link is made by hand to be read one way here and another there
    private static Dictionary<string, string>? ReadQuery(string query)
    {
        var fragment = query.IndexOf('#');
        if (fragment >= 0)
            query = query[..fragment];

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = Unescape(equals < 0 ? pair : pair[..equals]);
            var value = Unescape(equals < 0 ? "" : pair[(equals + 1)..]);
            if (name == null || value == null || !values.TryAdd(name, value))
                return null;
        }

        return values;
    }

    private static string? Unescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    // The connect form checks the host as it does for a typed one; this only keeps out what cannot be a host
    private static string? Host(string? text)
    {
        var host = text?.Trim();
        return string.IsNullOrEmpty(host) || host.Length > 253 || host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)
                                                                               || c is '/' or '@' or '?' or '#')
            ? null
            : host;
    }

    private static bool TryReadPort(string? text, out int? port)
    {
        port = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value is < 1 or > 65535)
            return false;

        port = value;
        return true;
    }

    // SHA-256 in hex, as the transport reports it; "sha256:" and separators are accepted. Anything else is dropped,
    // so a broken fingerprint asks about the certificate as usual instead of failing the link
    private static string? Fingerprint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var value = text.Trim();
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            value = value["sha256:".Length..];
        var hex = new string(value.Where(c => c is not (':' or '-' or ' ')).ToArray()).ToUpperInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static bool IsCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Length <= 40 && code.All(c => char.IsLetterOrDigit(c) || c is '-' or ' ');
}
