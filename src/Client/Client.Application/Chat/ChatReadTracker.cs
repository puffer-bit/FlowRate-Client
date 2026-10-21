using Client.Core.Chat;

namespace Client.Application.Chat;

/// <summary>
/// The unread counts of the chats of one connection, as the list of chats shows them.
/// The server keeps the read positions; this keeps its last answer and moves it ahead locally:
/// a message of another user adds to the counts unless its chat is being read, an own message
/// and <see cref="MarkRead"/> clear them. A position moved here reaches the server after a short
/// pause, so scrolling through a chat sends one request, not one per message.
/// Safe to call from any thread; <see cref="StateChanged"/> is raised outside the lock, on the
/// calling thread.
/// </summary>
public sealed class ChatReadTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<int, ChatReadState> _states = new();

    // Positions moved here and not sent yet
    private readonly Dictionary<int, int> _unsent = new();
    private readonly TimeSpan _sendDelay;

    private Func<int, int, Task>? _send;
    private int _currentUserId;
    private int? _readingChatId;

    /// <summary>
    /// Bumped by <see cref="Detach"/>, so a send scheduled for the previous connection is dropped.
    /// </summary>
    private int _generation;

    /// <summary>
    /// The counts of a chat changed. Raised on the thread of the call that changed them.
    /// </summary>
    public event Action<ChatReadState>? StateChanged;

    public ChatReadTracker(TimeSpan sendDelay)
    {
        _sendDelay = sendDelay;
    }

    /// <param name="send">Sends a read position of a chat to the server.</param>
    public void Attach(int currentUserId, Func<int, int, Task> send)
    {
        lock (_lock)
        {
            _currentUserId = currentUserId;
            _send = send;
        }
    }

    /// <summary>
    /// Forgets the connection: positions not sent yet are dropped, the server still has the
    /// ones it got, and the next <see cref="Load"/> starts from them.
    /// </summary>
    public void Detach()
    {
        lock (_lock)
        {
            _send = null;
            _states.Clear();
            _unsent.Clear();
            _readingChatId = null;
            _generation++;
        }
    }

    public ChatReadState? Get(int chatId)
    {
        lock (_lock)
            return _states.GetValueOrDefault(chatId);
    }

    /// <summary>
    /// Takes the states the server answered with on connecting; a position moved here meanwhile
    /// and not sent yet wins over an older one from the server.
    /// </summary>
    public void Load(IReadOnlyList<ChatReadState> states)
    {
        var changed = new List<ChatReadState>(states.Count);
        lock (_lock)
        {
            _states.Clear();
            foreach (var state in states)
            {
                var kept = _unsent.TryGetValue(state.ChatId, out var position) && position > state.LastReadMessageId
                    ? Cleared(state, position)
                    : state;
                _states[state.ChatId] = kept;
                changed.Add(kept);
            }
        }

        foreach (var state in changed)
            StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Marks the chat the user has on screen and scrolled to its newest messages, or clears the
    /// mark of <paramref name="chatId"/>. New messages of that chat do not add to its counts.
    /// </summary>
    public void SetReading(int chatId, bool isReading)
    {
        lock (_lock)
        {
            if (isReading)
                _readingChatId = chatId;
            else if (_readingChatId == chatId)
                _readingChatId = null;
        }
    }

    public void OnMessageAdded(int chatId, ChatMessageModel message)
    {
        if (message.IsSystem)
            return;

        ChatReadState updated;
        lock (_lock)
        {
            // A chat created after the counts were loaded starts with nothing read, as on the server
            var state = _states.GetValueOrDefault(chatId) ?? new ChatReadState(chatId, 0, 0, 0);
            if (message.Id <= state.LastReadMessageId)
                return;

            // As on the server, writing to a chat reads everything before it
            if (message.SenderUserId == _currentUserId)
                updated = Cleared(state, message.Id);
            else if (_readingChatId == chatId)
                return;
            else
                updated = state with
                {
                    UnreadCount = Math.Min(state.UnreadCount + 1, ChatReadState.MaxCount),
                    MentionCount = MentionsMe(message)
                        ? Math.Min(state.MentionCount + 1, ChatReadState.MaxCount)
                        : state.MentionCount
                };

            _states[chatId] = updated;
        }

        StateChanged?.Invoke(updated);
    }

    /// <summary>
    /// A position the server reports, moved here or on another device. One behind what this
    /// device has moved to already is an answer to an older request and is ignored.
    /// </summary>
    public void OnServerState(ChatReadState state)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(state.ChatId, out var known) && known.LastReadMessageId > state.LastReadMessageId)
                return;

            _states[state.ChatId] = state;
            if (_unsent.TryGetValue(state.ChatId, out var position) && position <= state.LastReadMessageId)
                _unsent.Remove(state.ChatId);
        }

        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// The user has seen the chat up to <paramref name="messageId"/>, its newest message.
    /// </summary>
    public void MarkRead(int chatId, int messageId)
    {
        ChatReadState updated;
        bool schedule;
        int generation;
        lock (_lock)
        {
            var state = _states.GetValueOrDefault(chatId) ?? new ChatReadState(chatId, 0, 0, 0);
            if (messageId <= state.LastReadMessageId && state.UnreadCount == 0 && state.MentionCount == 0)
                return;

            updated = Cleared(state, Math.Max(messageId, state.LastReadMessageId));
            _states[chatId] = updated;

            if (updated.LastReadMessageId <= state.LastReadMessageId && !_unsent.ContainsKey(chatId))
            {
                schedule = false;
            }
            else
            {
                schedule = !_unsent.ContainsKey(chatId);
                _unsent[chatId] = updated.LastReadMessageId;
            }

            generation = _generation;
        }

        StateChanged?.Invoke(updated);

        if (schedule)
            _ = SendLaterAsync(chatId, generation);
    }

    private async Task SendLaterAsync(int chatId, int generation)
    {
        if (_sendDelay > TimeSpan.Zero)
            await Task.Delay(_sendDelay).ConfigureAwait(false);

        Func<int, int, Task>? send;
        int position;
        lock (_lock)
        {
            if (generation != _generation || !_unsent.Remove(chatId, out position))
                return;

            send = _send;
        }

        if (send == null)
            return;

        try
        {
            await send(chatId, position).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The position stays where the server has it; the counts are taken again on connecting
        }
    }

    private bool MentionsMe(ChatMessageModel message) =>
        message.MentionsEveryone || message.MentionedUserIds.Contains(_currentUserId);

    private static ChatReadState Cleared(ChatReadState state, int position) =>
        state with { LastReadMessageId = position, UnreadCount = 0, MentionCount = 0 };
}
