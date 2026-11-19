namespace Client.Application.Chat;

/// <summary>Input activity is renewed by edits, never by a draft simply remaining in the input.</summary>
public sealed class ChatTypingActivity
{
    public static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private DateTimeOffset _lastInput;
    private DateTimeOffset _lastPulse;
    private bool _active;

    /// <returns>The state to send, or null when a pulse is unnecessary.</returns>
    public bool? InputChanged(DateTimeOffset now, bool hasText)
    {
        if (!hasText)
            return Stop();
        _lastInput = now;
        if (_active && now - _lastPulse < PulseInterval)
            return null;
        _active = true;
        _lastPulse = now;
        return true;
    }

    public bool? Expire(DateTimeOffset now) => _active && now - _lastInput >= Timeout ? Stop() : null;

    public bool? Stop()
    {
        if (!_active)
            return null;
        _active = false;
        return false;
    }
}
