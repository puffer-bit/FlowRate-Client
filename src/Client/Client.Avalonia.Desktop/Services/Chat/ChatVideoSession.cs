using System;
using Client.Services;
using System.Runtime.InteropServices;
using Client.Core.Media;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.GStreamer.Video;
using Client.Application.Ports.Repositories;
using Client.ViewModels.Sessions.Chat;

namespace Client.Services.Chat;

/// <summary>Owns video playback independently of virtualized message cards. Accessed on the UI thread.</summary>
public sealed class ChatVideoSession : IDisposable, IChatVideoSession
{
    private readonly IAudioSettingsRepository? _settings;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly ILogger<ChatVideoSession>? _logger;
    private readonly DispatcherTimer _timer;
    private Task? _initialization;
    private ChatVideoPlayback? _playback;
    private CancellationTokenSource? _loading;
    private (string Coordinator, int FileId)? _key;
    private bool _disposed;
    private readonly ChatMediaPlayback? _global;

    public event Action? Changed;
    public double Volume { get; private set; } = 1;
    public bool IsLoading => _loading != null;
    /// <summary>True while a video that plays as it downloads waits for its bytes.</summary>
    public bool IsBuffering => _playback?.IsWaiting == true;
    /// <summary>The share of a video that plays as it downloads kept so far, or null for a cached one.</summary>
    public double? StreamProgress => _playback != null ? _source?.Download?.Progress : null;
    public bool IsPlaying => _playback?.IsPlaying == true;
    public bool HasError { get; private set; }
    public double Position => _playback is { HasEnded: true } ended ? ended.Duration : _playback?.Position ?? 0;
    public double Duration => _playback?.Duration ?? 0;

    public event Action<RawVideoFrame>? FrameReady;
    private readonly object _frameLock = new();
    // The last picture, for redisplay after the presenter moves: a reference to a YUV frame, or a copy of BGRA pixels
    private GpuVideoFrame? _lastGpu;
    private byte[]? _lastPixels;
    private RawVideoFrame _lastFrame;
    private ChatPlaybackSource? _source;
    private ChatFileVM? _file;

    private void DeliverFrame(RawVideoFrame frame)
    {
        lock (_frameLock)
        {
            if (frame.Gpu != null)
            {
                _lastGpu?.Dispose();
                _lastGpu = frame.Gpu.Retain();
                _lastPixels = null;
            }
            else
            {
                _lastGpu?.Dispose();
                _lastGpu = null;
                var length = checked(frame.Stride * frame.Height);
                if (_lastPixels?.Length != length)
                    _lastPixels = new byte[length];
                Marshal.Copy(frame.DataPtr, _lastPixels, 0, length);
            }
            _lastFrame = frame;
            FrameReady?.Invoke(frame);
        }
    }

    // Restores the paused picture after its presenter moves between windows.
    public void RedisplayFrame()
    {
        lock (_frameLock)
        {
            if (_lastGpu != null)
            {
                FrameReady?.Invoke(_lastFrame with { DataPtr = IntPtr.Zero, Gpu = _lastGpu });
                return;
            }
            if (_lastPixels == null)
                return;
            var pinned = GCHandle.Alloc(_lastPixels, GCHandleType.Pinned);
            try { FrameReady?.Invoke(_lastFrame with { DataPtr = pinned.AddrOfPinnedObject() }); }
            finally { pinned.Free(); }
        }
    }

    private void ClearLastFrame()
    {
        lock (_frameLock)
        {
            _lastGpu?.Dispose();
            _lastGpu = null;
            _lastPixels = null;
        }
    }

    // The OpenGL presenter could not show YUV frames after all: the same video continues as BGRA.
    private void OnSurfaceFailed(VideoSurfaceKinds kinds)
    {
        if ((kinds & VideoSurfaceKinds.YuvMemory) == 0)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _playback is not { DeliversYuv: true } playback || _source == null)
                return;
            var (position, playing) = (playback.Position, playback.IsPlaying);
            _playback = null;
            playback.Dispose();
            ClearLastFrame();
            try
            {
                _playback = Open(_source, Volume, position, playing);
                Refresh();
            }
            catch (Exception exception) { Fail(exception); }
        });
    }

    public ChatVideoSession(IAudioSettingsRepository? settings, ILogger<ChatVideoSession>? logger = null, ChatMediaPlayback? global = null)
    {
        _global = global;
        _settings = settings;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Refresh();
        VideoSurfaceSupport.Failed += OnSurfaceFailed;
    }

    public bool IsCurrent(ChatFileVM? file) => file != null && _key == file.PlaybackKey;

    public Task InitializeAsync() => _initialization ??= LoadVolumeAsync();

    private async Task LoadVolumeAsync()
    {
        if (_settings == null)
            return;
        try
        {
            var volume = await _settings.GetChatVideoVolumeAsync();
            Volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 1;
        }
        catch (Exception exception) { _logger?.LogWarning(exception, "Could not load chat video volume."); }
        if (!_disposed)
            Changed?.Invoke();
    }

    public async Task SetVolumeAsync(double volume)
    {
        await InitializeAsync();
        if (_disposed)
            return;
        Volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 1;
        try { _playback?.SetVolume(Volume); }
        catch (Exception exception) { Fail(exception); }
        Changed?.Invoke();

        if (_settings == null)
            return;

        // Serialize writes so an older slider release cannot overwrite a newer one.
        var savedVolume = Volume;
        await _saveLock.WaitAsync();
        try { await _settings.UpdateChatVideoVolumeAsync(savedVolume); }
        catch (Exception exception) { _logger?.LogWarning(exception, "Could not save chat video volume."); }
        finally { _saveLock.Release(); }
    }

    public async Task ToggleAsync(ChatFileVM file)
    {
        if (_disposed)
            return;
        if (IsCurrent(file) && IsLoading)
        {
            Stop();
            return;
        }
        if (IsCurrent(file) && _playback != null)
        {
            try
            {
                _playback.SetPlaying(!_playback.IsPlaying);
                Refresh();
            }
            catch (Exception exception) { Fail(exception); }
            return;
        }

        Stop();
        if (_global != null && !_global.Begin(this, file))
            return;
        _key = file.PlaybackKey;
        using var cancellation = new CancellationTokenSource();
        _loading = cancellation;
        Changed?.Invoke();
        try
        {
            await InitializeAsync();
            // Another video card may have changed the saved volume since this one was attached.
            await LoadVolumeAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            var source = await file.OpenPlaybackAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                source?.Dispose();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            if (source == null)
            {
                Stop();
                return;
            }
            _source = source;
            _file = file;
            _playback = Open(source, Volume);
            _timer.Start();
            Refresh();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(_loading, cancellation))
                Fail(exception);
        }
        finally
        {
            if (ReferenceEquals(_loading, cancellation))
            {
                _loading = null;
                Changed?.Invoke();
            }
        }
    }

    public void Seek(ChatFileVM file, double position)
    {
        if (!IsCurrent(file))
            return;
        try
        {
            _playback?.Seek(position);
            Changed?.Invoke();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void Refresh()
    {
        try { _playback?.Update(); }
        catch (Exception exception) { Fail(exception); }
        Changed?.Invoke();
    }

    private ChatVideoPlayback Open(ChatPlaybackSource source, double volume, double startAt = 0, bool play = true) =>
        source.Download is { } download
            ? new ChatVideoPlayback(download, source.FileName, DeliverFrame, volume, startAt, play)
            : new ChatVideoPlayback(source.Path!, DeliverFrame, volume, startAt, play);

    private void Fail(Exception exception)
    {
        _logger?.LogWarning(exception, "Chat video playback failed.");
        if (_source?.Download?.Failure is { } failure)
            _file?.ReportPlaybackFailure(failure);
        ReleasePlayback();
        HasError = true;
        Changed?.Invoke();
    }

    private void ReleasePlayback()
    {
        _loading?.Cancel();
        _loading = null;
        _timer.Stop();
        _playback?.Dispose();
        _playback = null;
        // After the player, which may still be reading from it
        _source?.Dispose();
        _source = null;
        _file = null;
        ClearLastFrame();
    }

    public void Stop()
    {
        ReleasePlayback();
        _key = null;
        HasError = false;
        _global?.End(this);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        VideoSurfaceSupport.Failed -= OnSurfaceFailed;
        Stop();
    }
}
