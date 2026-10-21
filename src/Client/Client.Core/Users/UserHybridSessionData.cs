namespace Client.Core.Users;

public class UserHybridSessionData
{
    public int Volume { get; init; } = 100;
    public bool IsVolumeOverrideEnabled { get; init; }
    public bool IsMuted { get; init; }

    // The sound of the screen the user shows, apart from the voice
    public int ScreenVolume { get; init; } = 100;
    public bool IsScreenVolumeOverrideEnabled { get; init; }
    public bool IsScreenMuted { get; init; }

    public bool IsBlocked { get; init; }
}