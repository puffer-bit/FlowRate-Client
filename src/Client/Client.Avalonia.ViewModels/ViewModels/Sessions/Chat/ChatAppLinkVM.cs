using System.Globalization;
using Client.Application.Links;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Services;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The card of a <c>flowrate://</c> link in a message, made on the client from the link itself: what it asks for and
/// a button to do it. An approval of a device gets a warning instead of a button: whoever sent it wants their device
/// in the account of the one who presses it.
/// </summary>
public sealed class ChatAppLinkVM
{
    private ChatAppLinkVM(FlowRateLink link, string address, bool canOpen)
    {
        Link = link;
        Address = address;
        CanOpen = canOpen && link is not DeviceLinkApproval;
    }

    public FlowRateLink Link { get; }

    /// <summary>The link as the message spells it.</summary>
    public string Address { get; }

    /// <summary>Whether the card has its button: not for an approval of a device, nor above the input.</summary>
    public bool CanOpen { get; }

    public bool IsWarning => Link is DeviceLinkApproval;

    public string Title => Link switch
    {
        ConnectLink => Strings.Get("chat.appLink.connect.title"),
        LoginLink => Strings.Get("chat.appLink.login.title"),
        _ => Strings.Get("chat.appLink.device.title")
    };

    /// <summary>The server of the link; empty for an approval of a device that names none.</summary>
    public string Server => Link switch
    {
        ConnectLink connect => ServerName(connect.Host, connect.Port),
        LoginLink login => ServerName(login.Host, login.Port),
        DeviceLinkApproval { Host: { } host } approval => ServerName(host, approval.Port),
        _ => ""
    };

    public bool HasServer => Server.Length > 0;

    public string? Description => Link switch
    {
        ConnectLink { Fingerprint: not null } or LoginLink { Fingerprint: not null } =>
            Strings.Get("chat.appLink.certificate"),
        LoginLink => Strings.Get("chat.appLink.login.description"),
        DeviceLinkApproval => Strings.Get("chat.appLink.device.warning"),
        _ => null
    };

    public bool HasDescription => Description != null;

    /// <summary>
    /// The code of an approval of a device, shown on its own: the owner of the device types it in by hand, since the
    /// card cannot approve it.
    /// </summary>
    public string? Code => (Link as DeviceLinkApproval)?.Code;

    public bool HasCode => Code != null;

    public string CodeLabel => Strings.Get("chat.appLink.device.code");

    public string ActionText => Link is LoginLink
        ? Strings.Get("chat.appLink.login.action")
        : Strings.Get("chat.appLink.connect.action");

    /// <summary>Does what the link asks, up to the form the user confirms.</summary>
    public void Open()
    {
        if (CanOpen)
            Open(Address);
    }

    /// <summary>
    /// Hands a link of a message to the application as the system hands the ones opened outside it. An approval of a
    /// device is dropped: it never opens from a message.
    /// </summary>
    public static void Open(string address)
    {
        if (FlowRateLinks.Parse(address) is not null and not DeviceLinkApproval)
            IncomingLinks.Post(address);
    }

    /// <summary>
    /// The card of the first link of the application in the text that the application understands; null when there
    /// is none.
    /// </summary>
    /// <param name="canOpen">False above the input, where the card only shows what the message will carry.</param>
    public static ChatAppLinkVM? FromText(string? text, bool canOpen = true)
    {
        foreach (var link in ChatAppLinks.FindAll(text))
        {
            var address = link.Uri.OriginalString;
            // A link into the settings opens from a message, but has no card
            if (FlowRateLinks.Parse(address) is { } parsed and not SettingsLink)
                return new ChatAppLinkVM(parsed, address, canOpen);
        }

        return null;
    }

    private static string ServerName(string host, int? port) =>
        port is { } value
            ? (host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host) + ":" + value.ToString(CultureInfo.InvariantCulture)
            : host;
}
