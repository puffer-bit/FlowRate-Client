namespace Client.Application.Sessions.Hybrid;

/// <summary>
/// Who speaks in the hybrid sessions of this client, for the speaking indicators of every list that
/// shows the peers. Only a joined session knows it, from the audio its transport receives and sends.
/// </summary>
public interface IVoiceActivityService
{
    /// <summary>
    /// Raised with the session id, the user id and whether the user speaks now, only on a change.
    /// Raised on any thread.
    /// </summary>
    event Action<int, int, bool>? Changed;

    bool IsSpeaking(int sessionId, int userId);

    void Set(int sessionId, int userId, bool isSpeaking);

    /// <summary>
    /// Reports everyone in the session silent, once its transport goes away.
    /// </summary>
    void Clear(int sessionId);
}
