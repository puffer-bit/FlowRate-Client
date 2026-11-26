namespace Client.LibWebRTC.Transport;

/// <summary>
/// Tells from the levels of a muted microphone that the user talks into it: a few loud readings close
/// together start it, a pause of a few seconds ends it. The level comes after the voice gate, so noise
/// that the gate keeps out does not count. Used by one thread at a time.
/// </summary>
internal sealed class MutedSpeechDetector
{
    // Readings come every 200 ms: three loud ones within two seconds are a phrase, not a cough
    private const int LoudReadingsToStart = 3;
    private static readonly TimeSpan StartWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QuietToEnd = TimeSpan.FromSeconds(3);

    private readonly Queue<long> _loudReadings = new();
    private long _lastLoud;

    public bool IsSpeaking { get; private set; }

    /// <param name="isLoud">The muted microphone was heard above the speaking level in this reading.</param>
    /// <returns>The new state when it changed.</returns>
    public bool? Update(bool isLoud, long now)
    {
        if (isLoud)
        {
            _lastLoud = now;
            _loudReadings.Enqueue(now);
        }

        while (_loudReadings.Count > 0 && now - _loudReadings.Peek() > (long)StartWindow.TotalMilliseconds)
            _loudReadings.Dequeue();

        if (!IsSpeaking && _loudReadings.Count >= LoudReadingsToStart)
        {
            IsSpeaking = true;
            return true;
        }

        if (IsSpeaking && now - _lastLoud > (long)QuietToEnd.TotalMilliseconds)
            return Reset();

        return null;
    }

    /// <returns>False when the user was speaking, null otherwise.</returns>
    public bool? Reset()
    {
        _loudReadings.Clear();
        if (!IsSpeaking)
            return null;

        IsSpeaking = false;
        return false;
    }
}
