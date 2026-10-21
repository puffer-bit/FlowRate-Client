namespace Client.Core.Settings;

/// <summary>What a hot key does. The values are saved, so they are never reused.</summary>
public enum HotKeyAction
{
    ToggleMicrophone = 1,
    ToggleSound = 2
}

/// <summary>How the hot keys are heard outside of the window of the application.</summary>
public enum GlobalHotKeysBackend
{
    /// <summary>Only while the window has the focus.</summary>
    None,

    /// <summary>The keyboards are read from /dev/input.</summary>
    Evdev,

    /// <summary>Raw key events of the X server, also of XWayland when the desktop lets them out.</summary>
    XInput,

    /// <summary>A low level keyboard hook of Windows.</summary>
    WindowsHook
}

/// <summary>
/// The keys of one action.
/// </summary>
/// <param name="Gesture">
/// Modifiers and one physical key, as "Ctrl+Shift+M" with the names of the Avalonia physical keys;
/// null when nothing is assigned.
/// </param>
/// <param name="IsGlobal">Heard in other applications as well, not only in the window.</param>
public sealed record HotKeyBinding(HotKeyAction Action, string? Gesture, bool IsGlobal = true);

/// <summary>
/// User preferences of the Hotkeys tab. A key is only listened to and never taken away: it still
/// types where it is pressed.
/// </summary>
public sealed record HotKeysSettings
{
    public static IReadOnlyList<HotKeyAction> Actions { get; } = [HotKeyAction.ToggleMicrophone, HotKeyAction.ToggleSound];

    public bool IsEnabled { get; init; } = true;

    public bool IsGlobalEnabled { get; init; } = true;

    /// <summary>One binding for each of <see cref="Actions"/>, none assigned by default.</summary>
    public IReadOnlyList<HotKeyBinding> Bindings { get; init; } =
        Actions.Select(action => new HotKeyBinding(action, null)).ToList();

    public HotKeyBinding Get(HotKeyAction action) =>
        Bindings.FirstOrDefault(binding => binding.Action == action) ?? new HotKeyBinding(action, null);

    public HotKeysSettings With(HotKeyBinding binding) => this with
    {
        Bindings = Actions.Select(action => action == binding.Action ? binding : Get(action)).ToList()
    };
}
