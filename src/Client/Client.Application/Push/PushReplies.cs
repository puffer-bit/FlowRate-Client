using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Client.Core.Push;

namespace Client.Application.Push;

/// <summary>
/// Sends the replies typed into chat notifications. It needs no connection to the coordinator, so it
/// works also in a process the system started for a push alone.
/// </summary>
public sealed class PushReplies
{
    private readonly IPushKeysRepository _keys;
    private readonly ITrustedCertificatesRepository _certificates;
    private readonly IPushReplyTransport _transport;

    public PushReplies(IPushKeysRepository keys, ITrustedCertificatesRepository certificates,
        IPushReplyTransport transport)
    {
        _keys = keys;
        _certificates = certificates;
        _transport = transport;
    }

    public async Task<PushReplyResult> SendAsync(string coordinatorInstanceId, int chatId, string text,
        CancellationToken cancellationToken = default)
    {
        // Normalized like a message typed into the chat
        if (ChatTextContent.FromUserInput(text) is not { } content)
            return PushReplyResult.Rejected;

        if (await _keys.GetReplyTargetAsync(coordinatorInstanceId) is not { } target)
            return PushReplyResult.NotRegistered;

        var certificate = await _certificates.GetAsync(target.Address, target.Port);

        return await _transport.SendAsync(target, certificate?.Fingerprint, chatId, content.Text, cancellationToken);
    }

    /// <summary>
    /// The "Mark as read" button of a notification: the chat is read up to its newest message shown.
    /// </summary>
    public async Task<PushReplyResult> MarkReadAsync(string coordinatorInstanceId, int chatId, int messageId,
        CancellationToken cancellationToken = default)
    {
        if (messageId <= 0)
            return PushReplyResult.Rejected;

        if (await _keys.GetReplyTargetAsync(coordinatorInstanceId) is not { } target)
            return PushReplyResult.NotRegistered;

        var certificate = await _certificates.GetAsync(target.Address, target.Port);

        return await _transport.MarkReadAsync(target, certificate?.Fingerprint, chatId, messageId, cancellationToken);
    }
}
