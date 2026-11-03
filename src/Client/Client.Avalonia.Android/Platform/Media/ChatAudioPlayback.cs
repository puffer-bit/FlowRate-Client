using System;
using Android.Media;

namespace Client.Droid.Platform.Media;

internal sealed class ChatAudioPlayback : IDisposable
{
    private readonly MediaPlayer _player = new();
    private bool _ended;
    private bool _failed;
    public bool IsPlaying => !_ended && _player.IsPlaying;
    public double Position => _ended ? Duration : _player.CurrentPosition / 1000d;
    public double Duration => _player.Duration / 1000d;

    public ChatAudioPlayback(string path, double volume)
    {
        try
        {
            using var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!.Build()!;
            _player.SetAudioAttributes(attributes);
            _player.Completion += (_, _) => _ended = true;
            _player.Error += (_, args) => { _failed = true; args.Handled = true; };
            _player.SetDataSource(path);
            _player.Prepare();
            SetVolume(volume);
            _player.Start();
        }
        catch { Dispose(); throw; }
    }

    public void SetVolume(double volume)
    {
        var gain = (float)Math.Clamp(volume, 0, 1);
        _player.SetVolume(gain, gain);
    }

    public void SetPlaying(bool playing)
    {
        if (playing)
        {
            if (_ended) Seek(0);
            _player.Start();
        }
        else _player.Pause();
    }

    public void Seek(double seconds)
    {
        _player.SeekTo((int)(Math.Clamp(seconds, 0, Duration) * 1000));
        _ended = false;
    }

    public void Update()
    {
        if (_failed) throw new InvalidOperationException("Android audio playback failed.");
    }

    public void Dispose()
    {
        _player.Release();
        _player.Dispose();
    }
}
