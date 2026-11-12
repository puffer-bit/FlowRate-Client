using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Client.Avalonia.Styles.Animation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Avalonia.Localization;
using Client.Services;
using Client.ViewModels.Sessions.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Droid.Views;

public partial class GlobalChatPlayer : UserControl
{
    private readonly ChatMediaPlayback? _global;
    private IChatMediaSession? _session => _global?.Current;
    private bool _updating;
    private bool _seeking;
    private bool _changingVolume;
    private bool _ready;
    private ChatFileVM? _file;
    private CancellationTokenSource? _hideCancellation;

    public GlobalChatPlayer() : this(App.Services?.GetService<ChatMediaPlayback>()) { }

    internal GlobalChatPlayer(ChatMediaPlayback? global)
    {
        _global = global;
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
        if (_session != null && _global?.File is ChatFileVM file)
            await _session.ToggleAsync(file);
    }

    private void Refresh()
    {
        if (!_ready)
            return;
        var current = _session != null;
        if (!current)
        {
            ResetInteraction();
            if (IsVisible && _hideCancellation == null)
                _ = HideAsync();
            return;
        }
        var wasLeaving = _hideCancellation != null;
        _hideCancellation?.Cancel();
        _hideCancellation = null;
        Presence.SetIsLeaving(this, false);
        IsVisible = true;
        if (wasLeaving)
            Presence.Play(this, Presence.GetAppear(this));
        if (!ReferenceEquals(_file, _global?.File))
        {
            ResetInteraction();
            _file = _global?.File;
        }
        FileNameText.Text = _global?.File?.FileName ?? string.Empty;
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
        var duration = _session?.Duration ?? 0;
        TimeText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
    }

    private void OnSeek(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating || _session == null || _global?.File is not ChatFileVM file)
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
        if (_global?.File is ChatFileVM file)
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
        if (_global != null)
            _global.Changed += Refresh;
        Motion.Changed += OnMotionChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_global != null)
            _global.Changed -= Refresh;
        Motion.Changed -= OnMotionChanged;
        HideImmediately();
        ResetInteraction();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task HideAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _hideCancellation = cancellation;
        Presence.SetIsLeaving(this, true);
        try
        {
            // Keep the last title and position visible until the shared leave effect finishes.
            await Task.Delay(Presence.GetLeaveTime(this), cancellation.Token);
            if (ReferenceEquals(_hideCancellation, cancellation))
                HideImmediately();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_hideCancellation, cancellation))
                _hideCancellation = null;
        }
    }

    private void HideImmediately()
    {
        _hideCancellation?.Cancel();
        _hideCancellation = null;
        IsVisible = false;
        Presence.SetIsLeaving(this, false);
        _file = null;
    }

    private void OnMotionChanged()
    {
        if (!Motion.IsEnabled && _hideCancellation != null)
            HideImmediately();
    }

    private void ResetInteraction()
    {
        _seeking = false;
        _changingVolume = false;
        if (_ready)
            VolumeButton.Flyout?.Hide();
    }

    private async void OnStop(object? sender, RoutedEventArgs e)
    {
        ResetInteraction();
        if (_global != null)
            await _global.ResetAsync();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        ResetInteraction();
        _global?.Stop();
    }
}
