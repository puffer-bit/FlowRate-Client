using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Avalonia.Localization;
using Client.Services.Chat;
using Client.ViewModels.Sessions.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Views.Sessions.Chat;

public partial class ChatAudioPlayer : UserControl
{
    private readonly ChatAudioSession? _session;
    private bool _updating;
    private bool _seeking;
    private bool _changingVolume;
    private bool _ready;

    public ChatAudioPlayer() : this(App.Services?.GetService<ChatAudioSession>()) { }

    internal ChatAudioPlayer(ChatAudioSession? session)
    {
        _session = session;
        InitializeComponent();
        SeekSlider.AddHandler(PointerPressedEvent, OnSeekPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, OnSeekReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        SeekSlider.AddHandler(PointerCaptureLostEvent, OnSeekCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        VolumeSlider.AddHandler(PointerPressedEvent, OnVolumePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        VolumeSlider.AddHandler(PointerReleasedEvent, OnVolumeReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        VolumeSlider.AddHandler(PointerCaptureLostEvent, OnVolumeCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        _ready = true;
        Refresh();
    }

    private async void OnPlay(object? sender, RoutedEventArgs e)
    {
        if (_session != null && DataContext is ChatFileVM file)
            await _session.ToggleAsync(file);
    }

    private void Refresh()
    {
        if (!_ready)
            return;
        var current = _session?.IsCurrent(DataContext as ChatFileVM) == true;
        var duration = current ? _session!.Duration : 0;
        var position = current ? _session!.Position : 0;
        _updating = true;
        try
        {
            if (!_seeking)
            {
                SeekSlider.Maximum = Math.Max(1, duration);
                SeekSlider.Value = position;
            }
            SeekSlider.IsEnabled = duration > 0;
            if (!_changingVolume)
            {
                VolumeSlider.Value = (_session?.Volume ?? 1) * 100;
                VolumeText.Text = $"{VolumeSlider.Value:0}%";
            }
        }
        finally { _updating = false; }
        UpdateTime(_seeking ? SeekSlider.Value : position);
        var playing = current && _session!.IsPlaying;
        var loading = current && _session!.IsLoading;
        CancelImage.IsVisible = loading;
        PlayImage.IsVisible = !playing && !loading;
        PauseImage.IsVisible = playing;
        PlaybackError.IsVisible = current && _session!.HasError;
        ToolTip.SetTip(PlayButton, Strings.Get(loading ? "chat.file.cancel"
            : playing ? "chat.audio.pause" : "chat.audio.play"));
    }

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private void UpdateTime(double position)
    {
        var duration = _session?.IsCurrent(DataContext as ChatFileVM) == true ? _session.Duration : 0;
        TimeText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
    }

    private void OnSeek(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating || _session == null || DataContext is not ChatFileVM file || !_session.IsCurrent(file))
            return;
        UpdateTime(e.NewValue);
        if (!_seeking)
            _session.Seek(file, e.NewValue);
    }

    private void OnSeekPressed(object? sender, PointerPressedEventArgs e)
    {
        if (SeekSlider.IsEnabled && e.GetCurrentPoint(SeekSlider).Properties.IsLeftButtonPressed)
            _seeking = true;
    }

    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
            CompleteSeek();
    }

    private void OnSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CompleteSeek();

    private void CompleteSeek()
    {
        if (!_seeking)
            return;
        _seeking = false;
        if (DataContext is ChatFileVM file)
            _session?.Seek(file, SeekSlider.Value);
    }

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _updating)
            return;
        VolumeText.Text = $"{e.NewValue:0}%";
        if (!_changingVolume)
            ApplyVolume();
    }

    private void OnVolumePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(VolumeSlider).Properties.IsLeftButtonPressed)
            _changingVolume = true;
    }

    private void OnVolumeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
            CompleteVolumeChange();
    }

    private void OnVolumeCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CompleteVolumeChange();

    private void CompleteVolumeChange()
    {
        if (!_changingVolume)
            return;
        _changingVolume = false;
        ApplyVolume();
    }

    private async void ApplyVolume()
    {
        if (_session != null)
            await _session.SetVolumeAsync(VolumeSlider.Value / 100);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_session != null)
        {
            _session.Changed += Refresh;
            _ = _session.InitializeAsync();
        }
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_session != null)
            _session.Changed -= Refresh;
        ResetInteraction();
        base.OnDetachedFromVisualTree(e);
    }

    private void ResetInteraction()
    {
        _seeking = false;
        _changingVolume = false;
        if (_ready)
            VolumeButton.Flyout?.Hide();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        ResetInteraction();
        base.OnDataContextChanged(e);
        Refresh();
    }
}
