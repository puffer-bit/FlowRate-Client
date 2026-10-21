using Client.Core.Push;

namespace Client.Application.Ports;

/// <summary>
/// Sends a reply typed into a notification straight to the coordinator, without its connection.
/// </summary>
public interface IPushReplyTransport
{
    /// <param name="trustedFingerprint">The certificate the user trusted for the address, when the system does not.</param>
    Task<PushReplyResult> SendAsync(PushReplyTarget target, string? trustedFingerprint, int chatId, string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the read position of the user in a chat up to <paramref name="messageId"/>.
    /// </summary>
    /// <inheritdoc cref="SendAsync"/>
    Task<PushReplyResult> MarkReadAsync(PushReplyTarget target, string? trustedFingerprint, int chatId,
        int messageId, CancellationToken cancellationToken = default);
}
