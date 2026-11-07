using System;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using Client.Core.Media;
using Client.GStreamer.Video;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Avalonia.Localization;
using Client.Services.Chat;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

public partial class ChatVideoPlayer : UserControl
{
    private readonly ChatVideoSession? _session;
    private readonly object _surfaceLock = new();
    private CancellationTokenSource? _previewCancellation;
    private Task? _previewTask;
    private volatile bool _attached;
    private volatile ChatFileVM? _file;
    private bool _updating;
    private bool _seeking;
    private bool _changingVolume;
    private bool _ready;
    private Window? _fullscreen;

    public ChatVideoPlayer() : this(App.Services?.GetService<ChatVideoSession>()) { }

    internal ChatVideoPlayer(ChatVideoSession? session)
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

    private void OnFullscreen(object? sender, RoutedEventArgs e)
    {
        if (_fullscreen != null)
        {
            _fullscreen.Close();
            return;
        }
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;
        ResetInteraction();
        var window = new Window
        {
            DataContext = DataContext,
            Title = (DataContext as ChatFileVM)?.FileName ?? string.Empty,
            WindowState = WindowState.FullScreen,
            Background = global::Avalonia.Media.Brushes.Black
        };
        _fullscreen = window;
        var inlineWidth = Width;
        var inlineHeight = Height;
        // Keep the virtualized card in place while its content belongs to the window.
        Width = Bounds.Width;
        Height = Bounds.Height;
        // Move the complete player so all controls keep the same playback session.
        Content = null;
        PlayerBorder.Width = double.NaN;
        PlayerLayout.RowDefinitions = new RowDefinitions("*,Auto");
        window.Content = PlayerBorder;
        window.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            args.Handled = true;
            window.Close();
        }, RoutingStrategies.Tunnel);
        window.Closed += (_, _) =>
        {
            window.Content = null;
            _fullscreen = null;
            Width = inlineWidth;
            Height = inlineHeight;
            PlayerBorder.Width = 400;
            PlayerLayout.RowDefinitions = new RowDefinitions("225,Auto");
            Content = PlayerBorder;
            _session?.RedisplayFrame();
        };
        window.Show(owner);
        _session?.RedisplayFrame();
    }

    private void OnVideoTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        OnPlay(sender, e);
    }

    private async void OnPlay(object? sender, RoutedEventArgs e)
    {
        if (_session == null || DataContext is not ChatFileVM file)
            return;
        _previewCancellation?.Cancel();
        if (_previewTask != null)
            await _previewTask;
        if (_attached && ReferenceEquals(DataContext, file))
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
            : playing ? "chat.video.pause" : "chat.video.play"));
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
        _attached = true;
        if (_session != null)
        {
            _session.Changed += Refresh;
            _session.FrameReady += OnFrame;
            _ = _session.InitializeAsync();
        }
        Refresh();
        EnsurePreview();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_session != null)
        {
            _session.Changed -= Refresh;
            _session.FrameReady -= OnFrame;
        }
        CancelPreview();
        _fullscreen?.Close();
        lock (_surfaceLock)
            VideoSurface.Clear();
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
        _fullscreen?.Close();
        CancelPreview();
        _file = DataContext as ChatFileVM;
        if (_ready)
            lock (_surfaceLock)
            VideoSurface.Clear();
        ResetInteraction();
        base.OnDataContextChanged(e);
        Refresh();
        EnsurePreview();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            EnsurePreview();
    }

    private void OnFrame(RawVideoFrame frame)
    {
        lock (_surfaceLock)
        {
            if (_attached && _session?.IsCurrent(_file) == true)
                VideoSurface.PushFrame(frame);
        }
    }

    private void EnsurePreview()
    {
        if (!MediaRuntime.IsEnabled)
            return;

        if (!_ready || !_attached || !IsEffectivelyVisible || DataContext is not ChatFileVM { IsVideo: true } file)
            return;
        _file = file;
        if (_session?.IsCurrent(file) == true)
        {
            _session.RedisplayFrame();
            return;
        }
        if (_previewTask != null)
            return;
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        _previewTask = LoadPreviewAsync(file, cancellation);
    }

    private async Task LoadPreviewAsync(ChatFileVM file, CancellationTokenSource cancellation)
    {
        try
        {
            // A recycled card may still be finishing cancellation of its download.
            while (file.IsDownloading)
                await Task.Delay(25, cancellation.Token);
            var path = await file.GetPlaybackPathAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (path == null)
                return;
            var preview = await ChatVideoPreview.LoadAsync(path, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_attached && ReferenceEquals(DataContext, file) && _session?.IsCurrent(file) != true)
                preview.DeliverTo(VideoSurface.PushFrame);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // A missing preview must not prevent normal playback or leave an unobserved task.
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
                _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelPreview()
    {
        _previewCancellation?.Cancel();
        _previewCancellation = null;
        _previewTask = null;
    }
}
