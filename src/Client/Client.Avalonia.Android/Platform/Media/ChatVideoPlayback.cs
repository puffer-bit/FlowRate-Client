using System;
using Client.Core.Media;
using Client.Droid.Platform.Media.WebRTC;
using Com.Flowrate.Rtc;

namespace Client.Droid.Platform.Media;

internal sealed class ChatVideoPlayback : IDisposable
{
    private readonly ChatVideoEngine _engine;
    private readonly PictureListener _listener;
    private bool _disposed;

    public bool IsPlaying => _engine.Playing;
    public double Position => _engine.Position / 1000d;
    public double Duration => _engine.Duration / 1000d;

    public ChatVideoPlayback(string path, double volume, Action<RawVideoFrame> onFrame)
    {
        _listener = new PictureListener(onFrame);
        try { _engine = new ChatVideoEngine(path, (float)volume, _listener); }
        catch { _listener.Dispose(); throw; }
    }

    public void SetVolume(double volume) => _engine.SetVolume((float)Math.Clamp(volume, 0, 1));
    public void SetPlaying(bool playing) => _engine.Playing = playing;
    public void Seek(double seconds) => _engine.Seek((int)(Math.Clamp(seconds, 0, Duration) * 1000));
    public void Update()
    {
        if (System.Threading.Volatile.Read(ref _listener.Error) is { } exception)
            throw new InvalidOperationException("Could not display the video frame.", exception);
        if (_engine.Error is { } error)
            throw new InvalidOperationException(error);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Close();
        _engine.Dispose();
        _listener.Dispose();
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
