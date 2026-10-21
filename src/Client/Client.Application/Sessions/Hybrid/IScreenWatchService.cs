namespace Client.Application.Sessions.Hybrid;

/// <summary>
/// Whose screens are watched in the hybrid sessions of this client, for every list that shows the
/// peers. A shared screen is not received until it is asked for, so the lists also ask through here:
/// the session that is joined answers the request.
/// </summary>
public interface IScreenWatchService
{
    /// <summary>
    /// Raised with the session id, the user id and whether the screen of the user is watched now,
    /// only on a change. Raised on any thread.
    /// </summary>
    event Action<int, int, bool>? Changed;

    /// <summary>
    /// Raised with the session id, the user id and whether the screen of the user should be watched.
    /// Raised on the thread of the caller of <see cref="Request"/>.
    /// </summary>
    event Action<int, int, bool>? Requested;

    bool IsWatching(int sessionId, int userId);

    void Set(int sessionId, int userId, bool isWatching);

    /// <summary>
    /// Reports nothing watched in the session, once its transport goes away.
    /// </summary>
    void Clear(int sessionId);

    /// <summary>
    /// Asks the joined session to start or stop watching. Without one nothing happens.
    /// </summary>
    void Request(int sessionId, int userId, bool isWatching);
}
