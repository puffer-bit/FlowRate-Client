using Shared.Enums;
using Shared.Enums.WebRTC;
using Client.Core.Sessions.Hybrid;

namespace Client.Core.Sessions;

/// <summary>
/// A session the user asks the coordinator to create.
/// </summary>
/// <param name="CategoryId">The category the session is put in, or null for the sessions outside any category.</param>
public sealed record NewSessionModel(string Name, int HostId, int Capacity, NewSessionOptions Options,
    int? CategoryId = null);

public abstract record NewSessionOptions
{
    public abstract SessionType SessionType { get; }
}

public sealed record NewHybridSessionOptions(
    WebRTCEngine EngineType,
    VideoResolutions Resolution = VideoResolutions.FHD,
    int? VideoBitrate = 2500,
    bool IsAudioTransferEnabled = false,
    bool IsDataChannelEnabled = false,
    bool IsSimulcastEnabled = false,
    bool IsTunnelingEnabled = false,
    bool IsProxyActive = false,
    bool DisableStun = false,
    bool DisableTurn = false,
    MediaSettings? Media = null
) : NewSessionOptions
{
    public override SessionType SessionType => SessionType.Hybrid;
}

public sealed record NewChatSessionOptions(
    string? Description = null,
    string? IconPath = null,
    bool IsEncrypted = false,
    bool IsAttachmentsAllowed = true,
    bool IsVoiceMessageAllowed = true,
    bool IsVideoMessagesAllowed = true,
    bool IsDelayedMessagesAllowed = true
) : NewSessionOptions
{
    public override SessionType SessionType => SessionType.Chat;
}
