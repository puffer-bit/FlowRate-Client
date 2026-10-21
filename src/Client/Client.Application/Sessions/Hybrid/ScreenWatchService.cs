namespace Client.Application.Sessions.Hybrid;

public sealed class ScreenWatchService : IScreenWatchService
{
    private readonly Lock _lock = new();
    private readonly HashSet<(int SessionId, int UserId)> _watched = [];

    public event Action<int, int, bool>? Changed;

    public event Action<int, int, bool>? Requested;

    public bool IsWatching(int sessionId, int userId)
    {
        lock (_lock)
            return _watched.Contains((sessionId, userId));
    }

    public void Set(int sessionId, int userId, bool isWatching)
    {
        bool isChanged;

        lock (_lock)
            isChanged = isWatching ? _watched.Add((sessionId, userId)) : _watched.Remove((sessionId, userId));

        if (isChanged)
            Changed?.Invoke(sessionId, userId, isWatching);
    }

    public void Clear(int sessionId)
    {
        List<(int SessionId, int UserId)> released;

        lock (_lock)
        {
            released = _watched.Where(item => item.SessionId == sessionId).ToList();
            _watched.ExceptWith(released);
        }

        foreach (var (_, userId) in released)
            Changed?.Invoke(sessionId, userId, false);
    }

    public void Request(int sessionId, int userId, bool isWatching) =>
        Requested?.Invoke(sessionId, userId, isWatching);
}
