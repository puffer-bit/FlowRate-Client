using System;
using System.IO;
using System.Threading;
using Android.Media;
using Client.Application.Chat;

namespace Client.Droid.Platform.Media;

/// <summary>Plays a chat audio file, cached or as it downloads. Accessed on the UI thread.</summary>
internal sealed class ChatAudioPlayback : IDisposable
{
    private readonly MediaPlayer _player = new();
    // Set when the audio plays while it downloads
    private readonly StreamDataSource? _source;
    private bool _prepared;
    private bool _playing = true;
    private bool _ended;
    private bool _failed;
    private double _volume;

    // The player is prepared in the background, so a download does not hold the UI thread
    public bool IsPlaying => _prepared ? !_ended && _player.IsPlaying : _playing;
    public double Position => !_prepared ? 0 : _ended ? Duration : _player.CurrentPosition / 1000d;
    public double Duration => _prepared ? _player.Duration / 1000d : 0;

    /// <summary>True while the audio waits for bytes that have not arrived yet.</summary>
    public bool IsWaiting => _source?.Download.IsWaiting == true;

    public ChatAudioPlayback(string path, double volume) : this((StreamDataSource?)null, volume) => Open(() => _player.SetDataSource(path));

    /// <summary>Plays an attachment while it downloads, which the caller keeps until this is disposed.</summary>
    public ChatAudioPlayback(StreamingDownload download, double volume) : this(new StreamDataSource(download), volume) =>
        Open(() => _player.SetDataSource(_source));

    private ChatAudioPlayback(StreamDataSource? source, double volume)
    {
        _source = source;
        _volume = volume;
    }

    private void Open(Action setDataSource)
    {
        try
        {
            using var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!.Build()!;
            _player.SetAudioAttributes(attributes);
            _player.Completion += (_, _) => _ended = true;
            _player.Error += (_, args) => { _failed = true; args.Handled = true; };
            _player.Prepared += (_, _) =>
            {
                _prepared = true;
                SetVolume(_volume);
                if (_playing)
                    _player.Start();
            };
            setDataSource();
            _player.PrepareAsync();
        }
        catch { Dispose(); throw; }
    }

    public void SetVolume(double volume)
    {
        _volume = volume;
        var gain = (float)Math.Clamp(volume, 0, 1);
        _player.SetVolume(gain, gain);
    }

    public void SetPlaying(bool playing)
    {
        _playing = playing;
        if (!_prepared)
            return;
        if (playing)
        {
            if (_ended) Seek(0);
            _player.Start();
        }
        else _player.Pause();
    }

    public void Seek(double seconds)
    {
        if (!_prepared)
            return;
        _player.SeekTo((int)(Math.Clamp(seconds, 0, Duration) * 1000));
        _ended = false;
    }

    public void Update()
    {
        if (_source?.Failure is { } failure)
            throw new IOException("The audio stopped downloading.", failure);
        if (_failed) throw new InvalidOperationException("Android audio playback failed.");
    }

    public void Dispose()
    {
        // The player waits for a read in progress when it is released
        _source?.Stop();
        _player.Release();
        _player.Dispose();
        _source?.Dispose();
    }

    /// <summary>Hands the player the bytes of the audio as they arrive; called on the player's own threads.</summary>
    private sealed class StreamDataSource(StreamingDownload download) : MediaDataSource
    {
        private readonly CancellationTokenSource _stopped = new();
        private Exception? _failure;

        public StreamingDownload Download => download;
        public Exception? Failure => Volatile.Read(ref _failure);
        public override long Size => download.Length;

        public override int ReadAt(long position, byte[]? buffer, int offset, int size)
        {
            if (size == 0)
                return 0;
            if (buffer == null || _stopped.IsCancellationRequested)
                return -1;
            try
            {
                var read = download.Read(position, buffer.AsSpan(offset, size), _stopped.Token);
                return read == 0 ? -1 : read;
            }
            catch (OperationCanceledException)
            {
                return -1;
            }
            catch (Exception exception)
            {
                // Exceptions must never escape into the player's thread
                Interlocked.CompareExchange(ref _failure, exception, null);
                return -1;
            }
        }

        public override void Close() { }

        public void Stop() => _stopped.Cancel();
    }
}
