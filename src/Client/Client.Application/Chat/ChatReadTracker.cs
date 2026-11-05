using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client.Application.Chat;

/// <summary>
/// Device-local read positions and unread counts, isolated by coordinator and account.
/// History and live messages share deduplication. Events are raised outside the lock.
/// </summary>
public sealed class ChatReadTracker(IChatReadStateRepository? repository = null,
    ILogger<ChatReadTracker>? logger = null)
{
    private readonly object _lock = new();
    private readonly Dictionary<int, ChatReadState> _states = new();
    private readonly Dictionary<int, Dictionary<int, bool>> _seen = new();
    private readonly ILogger _logger = logger ?? NullLogger<ChatReadTracker>.Instance;
    private string? _coordinatorId;
    private int _currentUserId;
    private int? _readingChatId;
    private int _generation;
    private Task _save = Task.CompletedTask;

    public event Action<ChatReadState>? StateChanged;

    public async Task AttachAsync(string coordinatorInstanceId, int currentUserId)
    {
        Detach();
        int generation;
        Task pending;
        lock (_lock)
        {
            generation = _generation;
            pending = _save;
        }
        await pending.ConfigureAwait(false);
        var states = repository == null ? [] : await repository.GetAsync(coordinatorInstanceId, currentUserId)
            .ConfigureAwait(false);
        lock (_lock)
        {
            if (generation != _generation)
                return;
            _coordinatorId = coordinatorInstanceId;
            _currentUserId = currentUserId;
            foreach (var state in states)
                _states[state.ChatId] = state;
        }
    }

    public void Detach()
    {
        lock (_lock)
        {
            _generation++;
            _coordinatorId = null;
            _states.Clear();
            _seen.Clear();
            _readingChatId = null;
        }
    }

    public ChatReadState? Get(int chatId)
    {
        lock (_lock)
            return _states.GetValueOrDefault(chatId);
    }

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

    public void OnMessageAdded(int chatId, ChatMessageModel message) => ApplyMessage(chatId, message, false);

    // History must not be treated as visible just because the user is currently at the bottom.
    public void OnHistoryMessage(int chatId, ChatMessageModel message) => ApplyMessage(chatId, message, true);

    private void ApplyMessage(int chatId, ChatMessageModel message, bool history)
    {
        if (message.IsSystem)
            return;
        ChatReadState updated;
        lock (_lock)
        {
            if (_coordinatorId == null)
                return;
            var state = _states.GetValueOrDefault(chatId) ?? new ChatReadState(chatId, 0, 0, 0);
            if (message.Id <= state.LastReadMessageId)
                return;
            if (!_seen.TryGetValue(chatId, out var seen))
                _seen[chatId] = seen = [];
            if (!seen.TryAdd(message.Id, message.MentionsEveryone || message.MentionedUserIds.Contains(_currentUserId)))
                return;

            if (message.SenderUserId == _currentUserId || (!history && _readingChatId == chatId))
            {
                updated = Advance(state, message.Id);
                Save(updated);
            }
            else
                updated = state with
                {
                    UnreadCount = Math.Min(state.UnreadCount + 1, ChatReadState.MaxCount),
                    MentionCount = message.MentionsEveryone || message.MentionedUserIds.Contains(_currentUserId)
                        ? Math.Min(state.MentionCount + 1, ChatReadState.MaxCount) : state.MentionCount
                };
            _states[chatId] = updated;
        }
        StateChanged?.Invoke(updated);
    }

    public void MarkRead(int chatId, int messageId)
    {
        ChatReadState updated;
        lock (_lock)
        {
            if (_coordinatorId == null)
                return;
            var state = _states.GetValueOrDefault(chatId) ?? new ChatReadState(chatId, 0, 0, 0);
            if (messageId <= state.LastReadMessageId && state.UnreadCount == 0 && state.MentionCount == 0)
                return;
            updated = Advance(state, Math.Max(messageId, state.LastReadMessageId));
            _states[chatId] = updated;
            Save(updated);
        }
        StateChanged?.Invoke(updated);
    }

    // Capture the account now: queued writes must survive detach and account switching.
    private void Save(ChatReadState state)
    {
        if (repository != null && _coordinatorId is { } coordinatorId)
            _save = SaveAsync(_save, coordinatorId, _currentUserId, state);
    }

    private async Task SaveAsync(Task previous, string coordinatorId, int userId, ChatReadState state)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await repository!.SetAsync(coordinatorId, userId, state).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save the local read position for chat {ChatId}", state.ChatId);
        }
    }

    private ChatReadState Advance(ChatReadState state, int position)
    {
        if (!_seen.TryGetValue(state.ChatId, out var seen))
            return state with { LastReadMessageId = position, UnreadCount = 0, MentionCount = 0 };
        foreach (var id in seen.Keys.Where(id => id <= position).ToArray())
            seen.Remove(id);
        return state with
        {
            LastReadMessageId = position,
            UnreadCount = Math.Min(seen.Count, ChatReadState.MaxCount),
            MentionCount = Math.Min(seen.Values.Count(mentioned => mentioned), ChatReadState.MaxCount)
        };
    }
}
