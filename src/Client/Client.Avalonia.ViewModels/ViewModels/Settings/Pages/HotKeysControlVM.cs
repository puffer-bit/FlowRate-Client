using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Core.Settings;
using Client.Services;
using Client.Services.HotKeys;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

/// <summary>
/// The hot keys: whether they are heard at all and in other applications, and the keys of each action.
/// Every change is in effect at once.
/// </summary>
public class HotKeysControlVM : ReactiveObject, ISettingVM, IDisposable
{
    public string Name => SettingText.TabName("hotKeys");
    public SettingTabType Type => SettingTabType.HotKeys;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("hotKeys",
        ("general", ["enabled", "global"]),
        ("actions", ["toggleMicrophone", "toggleSound"]));

    public bool AreHotKeysEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsGlobalEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IReadOnlyList<HotKeyItemVM> Items { get; }

    /// <summary>
    /// Wayland lets an application hear only the keys of its own windows; the hint says what lets
    /// the keys out. Shown always there, whether the keys are heard or not.
    /// </summary>
    public bool IsWayland { get; } = OperatingSystem.IsLinux()
                                     && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IHotKeysService _hotKeysService;

    // Set while the view model fills its own properties, so that only user changes get saved
    private bool _isUpdating = true;

    public HotKeysControlVM(IHotKeysService hotKeysService)
    {
        _hotKeysService = hotKeysService;
        Items = HotKeysSettings.Actions.Select(action => new HotKeyItemVM(action, this)).ToList();

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        this.WatchProperties(nameof(AreHotKeysEnabled), nameof(IsGlobalEnabled))
            .Where(_ => !_isUpdating)
            .Subscribe(_ => Save());

    }

    private Task LoadAsync()
    {
        Apply(_hotKeysService.Current);
        return Task.CompletedTask;
    }

    internal void Save()
    {
        if (_isUpdating)
            return;

        _ = _hotKeysService.SaveSettingsAsync(ToSettings());
    }

    /// <summary>
    /// Gives the keys to an action. An action that had the same keys loses them: one press does one thing.
    /// </summary>
    internal void Assign(HotKeyItemVM item, HotKeyGesture gesture)
    {
        foreach (var other in Items.Where(other => other != item && other.Gesture == gesture))
            other.SetGesture(null);

        item.SetGesture(gesture);
        Save();
    }

    /// <summary>
    /// The keys trigger nothing while the user presses the ones to assign.
    /// </summary>
    internal void SetRecording(HotKeyItemVM? recording)
    {
        foreach (var item in Items)
            item.IsRecording = item == recording;

        _hotKeysService.IsSuspended = recording != null;
    }

    private HotKeysSettings ToSettings() => new()
    {
        IsEnabled = AreHotKeysEnabled,
        IsGlobalEnabled = IsGlobalEnabled,
        // Whether the keys are heard in other applications is one switch for all of them
        Bindings = Items.Select(item => new HotKeyBinding(item.Action, item.Gesture?.ToString())).ToList()
    };

    private void Apply(HotKeysSettings settings)
    {
        _isUpdating = true;

        AreHotKeysEnabled = settings.IsEnabled;
        IsGlobalEnabled = settings.IsGlobalEnabled;
        foreach (var item in Items)
        {
            var binding = settings.Get(item.Action);
            item.SetGesture(HotKeyGesture.TryParse(binding.Gesture, out var gesture) ? gesture : null);
        }

        _isUpdating = false;
    }

    /// <summary>The defaults of the tab: the keys on, heard everywhere, nothing assigned.</summary>
    public async Task ResetAllAsync()
    {
        var defaults = new HotKeysSettings();

        SetRecording(null);
        Apply(defaults);
        await _hotKeysService.SaveSettingsAsync(defaults);
    }

    public void Dispose()
    {
        // A window closed while keys were being pressed does not leave the hot keys silenced
        SetRecording(null);
    }
}

/// <summary>One action of the Hotkeys tab and its keys.</summary>
public class HotKeyItemVM : ReactiveObject
{
    public HotKeyAction Action { get; }

    /// <summary>The key of the texts of the row, "hotKeys.toggleMicrophone".</summary>
    public string SettingKey { get; }

    public HotKeyGesture? Gesture
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IReadOnlyList<Client.Avalonia.Controls.Menus.GestureKey> Keycaps
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public bool HasGesture
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The key types a character, so the action is toggled while the user types it.</summary>
    public bool IsTypingKey
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The next keys pressed are assigned to the action.</summary>
    public bool IsRecording
    {
        get;
        internal set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> RecordCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ClearCommand { get; }

    private readonly HotKeysControlVM _owner;

    internal HotKeyItemVM(HotKeyAction action, HotKeysControlVM owner)
    {
        Action = action;
        _owner = owner;
        SettingKey = action switch
        {
            HotKeyAction.ToggleMicrophone => "hotKeys.toggleMicrophone",
            HotKeyAction.ToggleSound => "hotKeys.toggleSound",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        RecordCommand = ReactiveCommand.Create(() => _owner.SetRecording(IsRecording ? null : this));
        ClearCommand = ReactiveCommand.Create(() =>
        {
            _owner.SetRecording(null);
            SetGesture(null);
            _owner.Save();
        }, this.WatchProperty(nameof(HasGesture), item => item.HasGesture));
    }

    /// <summary>Called by the view with the keys pressed while recording.</summary>
    public void Assign(HotKeyGesture gesture)
    {
        _owner.SetRecording(null);
        _owner.Assign(this, gesture);
    }

    public void CancelRecording()
    {
        if (IsRecording)
            _owner.SetRecording(null);
    }

    internal void SetGesture(HotKeyGesture? gesture)
    {
        Gesture = gesture;
        HasGesture = gesture != null;
        IsTypingKey = gesture?.IsTyping == true;
        Keycaps = gesture?.ToKeycaps() ?? [];
    }
}
