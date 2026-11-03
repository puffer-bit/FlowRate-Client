using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Client.ViewModels.Sessions.Chat;

namespace Client.Droid.Views;

public partial class ChatVideoPlayer : UserControl
{
    private CancellationTokenSource? _loading;

    public ChatVideoPlayer()
    {
        InitializeComponent();
        VideoSurface.Failed += () => PlaybackError.IsVisible = true;
    }

    private async void OnPlay(object? sender, RoutedEventArgs e)
    {
        if (_loading != null)
        {
            Stop();
            return;
        }
        if (DataContext is not ChatFileVM file)
            return;
        if (VideoSurface.HasSource)
        {
            VideoSurface.Toggle();
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _loading = cancellation;
        PlaybackError.IsVisible = false;
        try
        {
            var path = await file.GetPlaybackPathAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (path != null)
                VideoSurface.Play(path);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            PlaybackError.IsVisible = true;
        }
        finally
        {
            if (ReferenceEquals(_loading, cancellation))
                _loading = null;
        }
    }

    private void Stop()
    {
        _loading?.Cancel();
        _loading = null;
        VideoSurface?.Stop();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        Stop();
        base.OnDataContextChanged(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }
}

/// <summary>Uses the Android media player and its touch playback controls.</summary>
public sealed class AndroidVideoHost : NativeControlHost
{
    private global::Android.Widget.VideoView? _video;
    private global::Android.Widget.MediaController? _controller;
    public event Action? Failed;
    public bool HasSource { get; private set; }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as global::Avalonia.Android.AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _video = new global::Android.Widget.VideoView(context);
        _controller = new global::Android.Widget.MediaController(context);
        _controller.SetAnchorView(_video);
        _video.SetMediaController(_controller);
        _video.Prepared += (_, _) => _video?.Start();
        _video.Error += (_, args) =>
        {
            args.Handled = true;
            Stop();
            Failed?.Invoke();
        };
        return new global::Avalonia.Android.AndroidViewControlHandle(_video);
    }

    public void Play(string path)
    {
        if (_video == null)
            throw new InvalidOperationException("Video surface is unavailable.");
        _video.SetVideoPath(path);
        HasSource = true;
        _video.Start();
    }

    public void Toggle()
    {
        if (_video?.IsPlaying == true)
            _video.Pause();
        else
            _video?.Start();
    }

    public void Stop()
    {
        _controller?.Hide();
        _video?.StopPlayback();
        HasSource = false;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Stop();
        _video?.Dispose();
        _video = null;
        _controller?.Dispose();
        _controller = null;
    }
}
