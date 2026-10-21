namespace Client.Application.Sessions.Hybrid;

public sealed class VoiceActivityService : IVoiceActivityService
{
    private readonly Lock _lock = new();
    private readonly HashSet<(int SessionId, int UserId)> _speaking = [];

    public event Action<int, int, bool>? Changed;

    public bool IsSpeaking(int sessionId, int userId)
    {
        lock (_lock)
            return _speaking.Contains((sessionId, userId));
    }

    public void Set(int sessionId, int userId, bool isSpeaking)
    {
        bool isChanged;

        lock (_lock)
            isChanged = isSpeaking ? _speaking.Add((sessionId, userId)) : _speaking.Remove((sessionId, userId));

        if (isChanged)
            Changed?.Invoke(sessionId, userId, isSpeaking);
    }

    public void Clear(int sessionId)
    {
        List<(int SessionId, int UserId)> silenced;

        lock (_lock)
        {
            silenced = _speaking.Where(item => item.SessionId == sessionId).ToList();
            _speaking.ExceptWith(silenced);
        }

        foreach (var (_, userId) in silenced)
            Changed?.Invoke(sessionId, userId, false);
    }
}
