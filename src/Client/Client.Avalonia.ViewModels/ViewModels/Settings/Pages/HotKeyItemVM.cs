using System;
using System.Collections.Generic;
using Client.Core.Settings;
using Client.Services;
using Client.Services.HotKeys;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

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
