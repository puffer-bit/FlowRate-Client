namespace Client.Core.Settings;

/// <summary>
/// User preferences of the Behavior tab. The remembered auto connection identity is data, not a
/// preference, and lives outside of this model.
/// </summary>
public class BehaviorSettings
{
    public bool KeepScreenOnWhileWatching { get; set; } = true;

    public bool IsAutoConnectEnabled { get; set; } = true;

    public bool IsFallbackConnectionEnabled { get; set; } = false;
    public bool IsFallbackConnectionWarningEnabled { get; set; } = true;
    public bool IsPropagatedConnectionWarningEnabled { get; set; } = true;

    public CloseButtonAction CloseButtonAction { get; set; } = CloseButtonAction.Ask;

    public bool IsUpdateCheckOnStartupEnabled { get; set; } = true;
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Release;

    /// <summary>
    /// Whether the status turns idle, then sleeping, while the computer gets no input; shown so only while the user
    /// picked online.
    /// </summary>
    public bool IsAutoIdleEnabled { get; set; } = true;

    /// <summary>Minutes without input before the status turns idle.</summary>
    public int IdleAfterMinutes { get; set; } = DefaultIdleAfterMinutes;

    /// <summary>Minutes without input before the status turns sleeping; 0 never does.</summary>
    public int SleepAfterMinutes { get; set; } = DefaultSleepAfterMinutes;

    /// <summary>Whether the status stays online during a call, when listening without touching anything.</summary>
    public bool KeepActiveDuringCalls { get; set; } = true;

    public const int DefaultIdleAfterMinutes = 10;
    public const int DefaultSleepAfterMinutes = 120;

    /// <summary>The reaction a double tap puts on a message.</summary>
    public string QuickReaction { get; set; } = DefaultQuickReaction;

    public const string DefaultQuickReaction = "❤️";
}
