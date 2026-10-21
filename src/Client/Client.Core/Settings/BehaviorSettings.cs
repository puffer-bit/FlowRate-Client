namespace Client.Core.Settings;

/// <summary>
/// User preferences of the Behavior tab. The remembered auto connection identity is data, not a
/// preference, and lives outside of this model.
/// </summary>
public class BehaviorSettings
{
    public bool IsAutoConnectEnabled { get; set; }

    public bool IsFallbackConnectionEnabled { get; set; } = false;
    public bool IsFallbackConnectionWarningEnabled { get; set; } = true;
    public bool IsPropagatedConnectionWarningEnabled { get; set; } = true;

    public CloseButtonAction CloseButtonAction { get; set; } = CloseButtonAction.Ask;

    public bool IsUpdateCheckOnStartupEnabled { get; set; } = true;
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Release;
}
