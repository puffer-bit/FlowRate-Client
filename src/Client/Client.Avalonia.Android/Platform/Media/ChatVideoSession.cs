using System;
using Client.Services;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports.Repositories;
using Client.Core.Media;
using Client.ViewModels.Sessions.Chat;
using Microsoft.Extensions.Logging;

namespace Client.Droid.Platform.Media;

/// <summary>Owns chat playback independently of virtualized message cards. Accessed on the UI thread.</summary>
public sealed class ChatVideoSession : IDisposable, IChatMediaSession
{
    private readonly IAudioSettingsRepository _settings;
    private readonly ILogger<ChatVideoSession> _logger;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Task? _initialization;
    private ChatVideoPlayback? _playback;
    private CancellationTokenSource? _loading;
    private (string Coordinator, int FileId)? _key;
    private bool _disposed;
    private readonly ChatMediaPlayback? _global;

    private readonly object _frameLock = new();
    private GpuVideoFrame? _lastFrame;
    public event Action<RawVideoFrame>? FrameReady;

    private void OnFrame(RawVideoFrame frame)
    {
        lock (_frameLock)
        {
            _lastFrame?.Dispose();
            _lastFrame = frame.Gpu?.Retain();
            FrameReady?.Invoke(frame);
        }
    }

    public void RedisplayFrame()
    {
        lock (_frameLock)
        {
            if (_lastFrame is { } frame)
                FrameReady?.Invoke(new RawVideoFrame(IntPtr.Zero, frame.Width, frame.Height, 0) { Gpu = frame });
        }
    }

    public event Action? Changed;
    public double Volume { get; private set; } = 1;
    public bool IsLoading => _loading != null;
    public bool IsPlaying => _playback?.IsPlaying == true;
    public bool HasError { get; private set; }
    public double Position => _playback?.Position ?? 0;
    public double Duration => _playback?.Duration ?? 0;

    public ChatVideoSession(IAudioSettingsRepository settings, ILogger<ChatVideoSession> logger, ChatMediaPlayback? global = null)
    {
        _global = global;
        _settings = settings;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Refresh();
    }

    public bool IsCurrent(ChatFileVM? file) => file != null && _key == file.PlaybackKey;

    public Task InitializeAsync() => _initialization ??= LoadVolumeAsync();

    private async Task LoadVolumeAsync()
    {
        try
        {
            var volume = await _settings.GetChatVideoVolumeAsync();
            Volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 1;
        }
        catch (Exception exception) { _logger.LogWarning(exception, "Could not load chat video volume."); }
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

        // Serialize writes so an older slider release cannot overwrite a newer one.
        var savedVolume = Volume;
        await _saveLock.WaitAsync();
        try { await _settings.UpdateChatVideoVolumeAsync(savedVolume); }
        catch (Exception exception) { _logger.LogWarning(exception, "Could not save chat video volume."); }
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
            cancellation.Token.ThrowIfCancellationRequested();
            var path = await file.GetPlaybackPathAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (path == null)
            {
                Stop();
                return;
            }
            _playback = new ChatVideoPlayback(path, Volume, OnFrame);
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

    private void Fail(Exception exception)
    {
        _logger.LogWarning(exception, "Chat video playback failed.");
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
        lock (_frameLock)
        {
            _lastFrame?.Dispose();
            _lastFrame = null;
        }
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
        Stop();
    }
}
