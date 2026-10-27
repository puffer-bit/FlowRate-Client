using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Reads the speech meters on a timer and reports the users who started or stopped speaking.
/// </summary>
internal sealed partial class SpeakingIndicator
{
    // How often the meters are read
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly int _sessionId;
    private readonly Action<long, HashSet<int>> _collect;
    private readonly Action<int, bool> _changed;
    private readonly ILogger _logger;

    private readonly Lock _speakingLock = new();
    private readonly HashSet<int> _speakingUsers = [];
    private System.Threading.Timer? _timer;

    /// <param name="collect">Adds the users who speak at the given timestamp to the set.</param>
    /// <param name="changed">Called for every user who started or stopped speaking.</param>
    public SpeakingIndicator(int sessionId, Action<long, HashSet<int>> collect, Action<int, bool> changed, ILogger logger)
    {
        _sessionId = sessionId;
        _collect = collect;
        _changed = changed;
        _logger = logger;
    }

    public void Start() => _timer = new System.Threading.Timer(_ => Update(), null, PollInterval, PollInterval);

    private void Update()
    {
        try
        {
            var speaking = new HashSet<int>();

            _collect(Stopwatch.GetTimestamp(), speaking);

            Publish(speaking);
        }
        catch (Exception exception)
        {
            LogSpeakingUpdateFailed(exception, _sessionId);
        }
    }

    private void Publish(HashSet<int> speaking)
    {
        List<(int UserId, bool IsSpeaking)> changes;

        lock (_speakingLock)
        {
            changes = _speakingUsers.Except(speaking).Select(userId => (userId, false))
                .Concat(speaking.Except(_speakingUsers).Select(userId => (userId, true)))
                .ToList();

            _speakingUsers.Clear();
            _speakingUsers.UnionWith(speaking);
        }

        foreach (var (userId, isSpeaking) in changes)
            _changed(userId, isSpeaking);
    }

    /// <summary>
    /// Stops reading the meters and reports everyone silent, since no audio arrives any more.
    /// </summary>
    public void Stop()
    {
        // Waits for a running round, so it cannot report a speaker after the silence below
        using (var stopped = new ManualResetEvent(false))
        {
            if (_timer?.Dispose(stopped) == true)
                stopped.WaitOne();
        }

        _timer = null;

        Publish([]);
    }
}
