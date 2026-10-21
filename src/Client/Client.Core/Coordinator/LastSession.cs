namespace Client.Core.Coordinator;

/// <summary>
/// Where the user was on a coordinator: what was on screen and which hybrid session they were in.
/// </summary>
/// <param name="ShownChatId">The chat on screen, or null when it showed something else.</param>
/// <param name="IsHybridSessionShown">The hybrid session was on screen.</param>
/// <param name="HybridSessionId">The hybrid session the user was in, shown or not.</param>
public sealed record LastSession(int? ShownChatId, bool IsHybridSessionShown, int? HybridSessionId)
{
    public bool IsEmpty => ShownChatId == null && HybridSessionId == null;
}
