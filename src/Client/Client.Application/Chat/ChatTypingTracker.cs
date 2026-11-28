namespace Client.Application.Chat;

/// <summary>Transient activity for every chat of one connection, independent of open views.</summary>
public sealed class ChatTypingTracker(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _lock = new();
    private readonly Dictionary<int, Dictionary<int, DateTimeOffset>> _chats = new();

    public void Update(int chatId, int userId, bool isTyping)
    {
        lock (_lock)
        {
            Prune();
            if (!isTyping)
            {
                if (_chats.TryGetValue(chatId, out var users))
                {
                    users.Remove(userId);
                    if (users.Count == 0) _chats.Remove(chatId);
                }
                return;
            }
            if (!_chats.TryGetValue(chatId, out var active))
                _chats[chatId] = active = new();
            active[userId] = _time.GetUtcNow() + ChatTypingActivity.Timeout;
        }
    }

    public IReadOnlyDictionary<int, DateTimeOffset> Get(int chatId)
    {
        lock (_lock)
        {
            Prune();
            return _chats.TryGetValue(chatId, out var users) ? new Dictionary<int, DateTimeOffset>(users) : new();
        }
    }

    public void RemoveChat(int chatId)
    {
        lock (_lock) _chats.Remove(chatId);
    }

    public void Clear()
    {
        lock (_lock) _chats.Clear();
    }

    // Also bounds storage for chats that were never opened, without a background timer.
    private void Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var (chatId, users) in _chats.ToArray())
        {
            foreach (var userId in users.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                users.Remove(userId);
            if (users.Count == 0) _chats.Remove(chatId);
        }
    }
}
