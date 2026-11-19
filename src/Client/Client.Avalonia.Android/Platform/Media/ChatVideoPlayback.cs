using System;
using System.IO;
using System.Threading;
using Client.Application.Chat;
using Client.Core.Media;
using Client.Droid.Platform.Media.WebRTC;
using Com.Flowrate.Rtc;

namespace Client.Droid.Platform.Media;

internal sealed class ChatVideoPlayback : IDisposable
{
    private readonly ChatVideoEngine _engine;
    private readonly PictureListener _listener;
    // Set when the video plays while it downloads
    private readonly StreamSource? _source;
    private bool _disposed;

    /// <summary>True while the video waits for bytes that have not arrived yet.</summary>
    public bool IsWaiting => _source?.Download.IsWaiting == true;

    public bool IsPlaying => _engine.Playing;
    public double Position => _engine.Position / 1000d;
    public double Duration => _engine.Duration / 1000d;

    public ChatVideoPlayback(string path, double volume, Action<RawVideoFrame> onFrame)
    {
        _listener = new PictureListener(onFrame);
        try { _engine = new ChatVideoEngine(path, (float)volume, _listener); }
        catch { _listener.Dispose(); throw; }
    }

    /// <summary>Plays an attachment while it downloads, which the caller keeps until this is disposed.</summary>
    public ChatVideoPlayback(StreamingDownload download, double volume, Action<RawVideoFrame> onFrame)
    {
        _listener = new PictureListener(onFrame);
        _source = new StreamSource(download);
        try { _engine = new ChatVideoEngine(_source, (float)volume, _listener); }
        catch { _listener.Dispose(); _source.Dispose(); throw; }
    }

    public void SetVolume(double volume) => _engine.SetVolume((float)Math.Clamp(volume, 0, 1));
    public void SetPlaying(bool playing) => _engine.Playing = playing;
    public void Seek(double seconds) => _engine.Seek((int)(Math.Clamp(seconds, 0, Duration) * 1000));
    public void Update()
    {
        if (System.Threading.Volatile.Read(ref _listener.Error) is { } exception)
            throw new InvalidOperationException("Could not display the video frame.", exception);
        if (_source?.Failure is { } failure)
            throw new IOException("The video stopped downloading.", failure);
        if (_engine.Error is { } error)
            throw new InvalidOperationException(error);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The player waits for a read in progress when it is released
        _source?.Stop();
        _engine.Close();
        _engine.Dispose();
        _listener.Dispose();
        _source?.Dispose();
    }

    /// <summary>Hands the player the bytes of the video as they arrive; called on the player's own threads.</summary>
    private sealed class StreamSource(StreamingDownload download) : Java.Lang.Object, ChatVideoEngine.ISource
    {
        private readonly CancellationTokenSource _stopped = new();
        private Exception? _failure;

        public StreamingDownload Download => download;
        public Exception? Failure => Volatile.Read(ref _failure);
        public long Size => download.Length;

        public int ReadAt(long position, byte[]? buffer, int offset, int size)
        {
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

        public void Stop() => _stopped.Cancel();
    }

    private sealed class PictureListener(Action<RawVideoFrame> onFrame) : Java.Lang.Object, ChatVideoEngine.IListener
    {
        public Exception? Error;

        public void OnPicture(VoicePeer.Picture? picture)
        {
            if (picture == null) return;
            try
            {
                using var frame = AndroidPictureFrame.Wrap(picture);
                onFrame(new RawVideoFrame(IntPtr.Zero, frame.Width, frame.Height, 0) { Gpu = frame });
            }
            catch (Exception exception)
            {
                // Exceptions must never escape through the native decoding thread.
                System.Threading.Volatile.Write(ref Error, exception);
            }
        }
    }
}
