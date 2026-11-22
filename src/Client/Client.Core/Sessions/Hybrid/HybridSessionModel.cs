using Shared.Enums.WebRTC;
using Client.Core.Sessions;

namespace Client.Core.Sessions.Hybrid;

public class HybridSessionModel : SessionModel
{
    public Guid? DirectCallId { get; init; }
    public DateTimeOffset? DirectCallStartedAt { get; init; }
    public required WebRTCEngine EngineType { get; init; }
    /// <summary>How many peers the session holds at most; not positive when it has no limit.</summary>
    public required int Capacity { get; init; }
    
    public bool IsApprovalRequested { get; init; }
    public MediaSettings Media { get; init; } = MediaSettings.Default;
}