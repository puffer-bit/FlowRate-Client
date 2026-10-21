using Client.Core.Settings;

namespace Client.Application.Settings;

/// <summary>
/// The hot keys: what is assigned, and the actions they trigger. The platform listens to the keys
/// and reports a press with <see cref="Raise"/>; everything else follows <see cref="SettingsChanged"/>
/// at once.
/// </summary>
public interface IHotKeysService
{
    /// <summary>The settings in effect, the defaults until <see cref="ApplyAsync"/>.</summary>
    HotKeysSettings Current { get; }

    /// <summary>How the keys are heard outside of the window, set by the platform.</summary>
    GlobalHotKeysBackend Backend { get; set; }

    /// <summary>
    /// The keys trigger nothing, while the user presses the ones to assign.
    /// </summary>
    bool IsSuspended { get; set; }

    /// <summary>The settings or <see cref="Backend"/> changed. Raised on the thread of the change.</summary>
    event Action? SettingsChanged;

    /// <summary>A key of an action was pressed and the keys are on.</summary>
    event Action<HotKeyAction>? Triggered;

    /// <summary>Puts the saved settings into effect. Called once at start.</summary>
    Task ApplyAsync();

    /// <summary>Saves the settings and puts them into effect at once.</summary>
    Task SaveSettingsAsync(HotKeysSettings settings);

    /// <summary>
    /// Whether a press of the keys of the action is heard by <see cref="Backend"/>, so the window
    /// must not report it once more.
    /// </summary>
    bool IsHeardGlobally(HotKeyAction action);

    /// <summary>Reports a press of the keys of an action.</summary>
    void Raise(HotKeyAction action);
}
