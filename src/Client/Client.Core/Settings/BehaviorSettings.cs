namespace Client.Core.Settings;

/// <summary>
/// User preferences of the Behavior tab. The remembered auto connection identity is data, not a
/// preference, and lives outside of this model.
/// </summary>
public class BehaviorSettings
{
    public bool KeepScreenOnWhileWatching { get; set; } = true;

    public bool IsAutoConnectEnabled { get; set; }

    public bool IsFallbackConnectionEnabled { get; set; } = false;
    public bool IsFallbackConnectionWarningEnabled { get; set; } = true;
    public bool IsPropagatedConnectionWarningEnabled { get; set; } = true;

    public CloseButtonAction CloseButtonAction { get; set; } = CloseButtonAction.Ask;

    public bool IsUpdateCheckOnStartupEnabled { get; set; } = true;
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Release;

    /// <summary>The reaction a double tap puts on a message.</summary>
    public string QuickReaction { get; set; } = DefaultQuickReaction;

    public const string DefaultQuickReaction = "❤️";
}
