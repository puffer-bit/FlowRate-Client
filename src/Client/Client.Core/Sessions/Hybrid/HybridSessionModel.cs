using Shared.Enums.WebRTC;
using Client.Core.Sessions;

namespace Client.Core.Sessions.Hybrid;

public class HybridSessionModel : SessionModel
{
    public required WebRTCEngine EngineType { get; init; }
    
    public bool IsAudioRequested { get; init; }
    public bool IsApprovalRequested { get; init; }
    public required int HostId { get; init; }
    public MediaSettings Media { get; init; } = MediaSettings.Default;
}