using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Droid.Platform.Media;

namespace Client.Droid.Views.Common.Media;

/// <summary>The same live device list in audio settings and in the call's quick controls.</summary>
public partial class CallAudioDevicePicker : UserControl
{
    private sealed record Option(string? Key, string Name)
    {
        public override string ToString() => Name;
    }

    private List<Option> _options = [];
    private bool _refreshing;
    private bool _attached;

    public CallAudioDevicePicker()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Design.IsDesignMode)
            return;
        _attached = true;
        AndroidCallAudioRouter.Instance.Changed += OnChanged;
        Strings.LanguageChanged += OnChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_attached)
        {
            AndroidCallAudioRouter.Instance.Changed -= OnChanged;
            Strings.LanguageChanged -= OnChanged;
            _attached = false;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_attached)
            Refresh();
    });

    private void Refresh()
    {
        var state = AndroidCallAudioRouter.Instance.State;
        List<Option> options = [new(null, Strings.Get("hybrid.audio.automatic"))];
        options.AddRange(state.Devices.Select(device => new Option(device.Key, DeviceName(device))));
        _refreshing = true;
        try
        {
            // Keep an open dropdown intact when only the active route changes.
            if (!_options.SequenceEqual(options))
            {
                _options = options;
                Devices.ItemsSource = _options;
            }
            Devices.SelectedItem = _options.FirstOrDefault(option => option.Key == state.PreferredKey) ?? _options[0];
            ActiveDevice.IsVisible = state.IsInCall;
            ActiveDevice.Text = state.ActiveDevice is { } active
                ? Strings.Format("hybrid.audio.active", ("device", DeviceName(active)))
                : Strings.Get("hybrid.audio.waiting");
            Switching.IsVisible = state.IsSwitching;
            Error.IsVisible = state.HasError;
            Retry.IsVisible = state.HasError;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && Devices.SelectedItem is Option option)
            AndroidCallAudioRouter.Instance.Select(option.Key);
    }

    private void OnRetryClick(object? sender, RoutedEventArgs e) =>
        AndroidCallAudioRouter.Instance.Select(AndroidCallAudioRouter.Instance.State.PreferredKey);

    private static string DeviceName(CallAudioDevice device)
    {
        var kind = device.Kind switch
        {
            CallAudioDeviceKind.Bluetooth => "bluetooth",
            CallAudioDeviceKind.Wired => "wired",
            CallAudioDeviceKind.Usb => "usb",
            CallAudioDeviceKind.Speaker => "speaker",
            CallAudioDeviceKind.Earpiece => "earpiece",
            _ => "other"
        };
        var label = Strings.Get($"hybrid.audio.{kind}");
        return device.Kind is CallAudioDeviceKind.Speaker or CallAudioDeviceKind.Earpiece
            || string.IsNullOrWhiteSpace(device.Name) ? label : $"{label}: {device.Name}";
    }
}
