using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client.Application.Chat;

/// <summary>
/// Read receipts of direct chats and groups on the attached coordinator. Keeps, for each chat, the furthest
/// position the other members have read up to: positions only grow, so an own message is read by someone
/// when its id is not above it. Also tells the server how far this device has read, one request per chat
/// at a time with the newest position, since the unread counts themselves are kept on the device.
/// Kept in memory only; attaching asks the server again. Events are raised outside the lock, on any thread.
/// </summary>
public sealed class ChatReceipts(ILogger<ChatReceipts>? logger = null)
{
    private readonly object _lock = new();
    private readonly ILogger _logger = logger ?? NullLogger<ChatReceipts>.Instance;
    private readonly Dictionary<int, int> _peers = new();
    private readonly Dictionary<int, int> _reported = new();
    private readonly Dictionary<int, int> _wanted = new();
    private readonly HashSet<int> _sending = new();
    private ICoordinatorSession? _session;
    private int _generation;

    /// <summary>The furthest position of the other members of a chat grew: (chat, position).</summary>
    public event Action<int, int>? PeerReadChanged;

    /// <summary>Another member read further; for the readers lists that are open.</summary>
    public event Action<ChatPeerRead>? PeerRead;

    public async Task AttachAsync(ICoordinatorSession session)
    {
        Detach();
        int generation;
        lock (_lock)
        {
            _session = session;
            generation = _generation;
        }
        session.ChatPeerRead += OnPeerRead;

        var states = await session.RequestChatReadStates().ConfigureAwait(false);
        if (!states.IsSuccess)
        {
            _logger.LogWarning("Loading the read receipts failed: {Result}", states.Error);
            return;
        }
        foreach (var state in states.Value)
            Advance(generation, state.ChatId, state.PeerReadMessageId);
    }

    public void Detach()
    {
        ICoordinatorSession? session;
        lock (_lock)
        {
            session = _session;
            _session = null;
            _generation++;
            _peers.Clear();
            _reported.Clear();
            _wanted.Clear();
            _sending.Clear();
        }
        if (session != null)
            session.ChatPeerRead -= OnPeerRead;
    }

    public int GetPeerRead(int chatId)
    {
        lock (_lock)
            return _peers.GetValueOrDefault(chatId);
    }

    /// <summary>
    /// This device has read a direct chat or a group up to <paramref name="messageId"/>. Positions not
    /// above the one reported are dropped; while a report is on the way, only the newest one waits.
    /// </summary>
    public void ReportRead(int chatId, int messageId)
    {
        ICoordinatorSession session;
        int generation;
        lock (_lock)
        {
            if (_session == null || messageId <= 0
                || messageId <= Math.Max(_reported.GetValueOrDefault(chatId), _wanted.GetValueOrDefault(chatId)))
                return;
            _wanted[chatId] = messageId;
            if (!_sending.Add(chatId))
                return;
            session = _session;
            generation = _generation;
        }
        _ = SendAsync(session, generation, chatId);
    }

    public async Task<RequestResult<IReadOnlyList<ChatMessageReader>>> RequestReaders(int chatId, int messageId)
    {
        ICoordinatorSession? session;
        lock (_lock)
            session = _session;
        return session == null
            ? new RequestResult<IReadOnlyList<ChatMessageReader>>(Shared.Enums.ErrorCode.NotConnected)
            : await session.RequestChatMessageReaders(chatId, messageId).ConfigureAwait(false);
    }

    private async Task SendAsync(ICoordinatorSession session, int generation, int chatId)
    {
        while (true)
        {
            int position;
            lock (_lock)
            {
                if (generation != _generation)
                    return;
                if (!_wanted.Remove(chatId, out position))
                {
                    _sending.Remove(chatId);
                    return;
                }
            }

            RequestResult<ChatReadState> result;
            try
            {
                result = await session.MarkChatRead(chatId, position).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Reporting the read position of chat {ChatId} failed", chatId);
                result = new RequestResult<ChatReadState>(Shared.Enums.ErrorCode.Internal);
            }

            if (result.IsSuccess)
            {
                lock (_lock)
                {
                    if (generation != _generation)
                        return;
                    _reported[chatId] = Math.Max(_reported.GetValueOrDefault(chatId), result.Value.LastReadMessageId);
                }
                Advance(generation, chatId, result.Value.PeerReadMessageId);
            }
            else
            {
                // Not retried at once: the next position read reports it again, a reconnect asks anew
                _logger.LogDebug("The server did not take the read position of chat {ChatId}: {Result}", chatId,
                    result.Error);
            }
        }
    }

    private void OnPeerRead(ChatPeerRead read)
    {
        int generation;
        lock (_lock)
            generation = _generation;
        Advance(generation, read.ChatId, read.LastReadMessageId);
        PeerRead?.Invoke(read);
    }

    private void Advance(int generation, int chatId, int position)
    {
        lock (_lock)
        {
            if (generation != _generation || position <= _peers.GetValueOrDefault(chatId))
                return;
            _peers[chatId] = position;
        }
        PeerReadChanged?.Invoke(chatId, position);
    }
}
