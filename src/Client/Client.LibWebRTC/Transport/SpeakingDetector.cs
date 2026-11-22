namespace Client.LibWebRTC.Transport;

/// <summary>
/// Turns the audio levels of the readings into who speaks: a user speaks from the first level above
/// <see cref="SpeakingLevel"/> until <see cref="SpeakingHold"/> passes without one.
/// </summary>
internal sealed class SpeakingDetector
{
    // The audio level, 0..1, above which a voice counts as speech, and how long it is held after it falls
    public const double SpeakingLevel = 0.02;
    private static readonly TimeSpan SpeakingHold = TimeSpan.FromMilliseconds(400);

    private readonly Lock _lock = new();
    private readonly Dictionary<int, long> _speakingUntil = [];

    /// <summary>
    /// Takes the users heard above <see cref="SpeakingLevel"/> in a reading, and returns who started
    /// and who stopped speaking.
    /// </summary>
    public List<(int UserId, bool IsSpeaking)> Update(IEnumerable<int> speakers)
    {
        var now = Environment.TickCount64;
        var holdUntil = now + (long)SpeakingHold.TotalMilliseconds;
        List<(int UserId, bool IsSpeaking)> changes = [];

        lock (_lock)
        {
            foreach (var speaker in speakers)
            {
                if (!_speakingUntil.ContainsKey(speaker))
                    changes.Add((speaker, true));

                _speakingUntil[speaker] = holdUntil;
            }

            foreach (var (userId, until) in _speakingUntil.ToList())
            {
                if (until > now)
                    continue;

                _speakingUntil.Remove(userId);
                changes.Add((userId, false));
            }
        }

        return changes;
    }

    /// <summary>Forgets everyone who speaks, and returns them.</summary>
    public List<int> Reset()
    {
        lock (_lock)
        {
            var speakers = _speakingUntil.Keys.ToList();
            _speakingUntil.Clear();
            return speakers;
        }
    }
}
