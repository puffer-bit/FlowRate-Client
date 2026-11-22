using Shared.Enums.WebRTC;
using Client.Core.Sessions;

namespace Client.Core.Sessions.Hybrid;

public class HybridSessionModel : SessionModel
{
    /// <summary>The chat call this session carries; null for server channels. The session ID is the chat ID.</summary>
    public Guid? CallId { get; init; }
    /// <summary>When a second member first joined the call; null while the caller waits alone.</summary>
    public DateTimeOffset? CallStartedAt { get; set; }
    /// <summary>The owner of the group whose call this is, who may kick from it; null in direct chats.</summary>
    public int? CallOwnerUserId { get; set; }
    public required WebRTCEngine EngineType { get; init; }
    /// <summary>How many peers the session holds at most; not positive when it has no limit.</summary>
    public required int Capacity { get; init; }
    
    public bool IsApprovalRequested { get; init; }
    public MediaSettings Media { get; init; } = MediaSettings.Default;
}