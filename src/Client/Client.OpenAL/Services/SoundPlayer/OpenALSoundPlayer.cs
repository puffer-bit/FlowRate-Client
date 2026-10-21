using Client.Application.Ports;
using Client.Application.Sounds;
using Silk.NET.OpenAL;

namespace Client.OpenAL.Services.SoundPlayer;

/// <summary>
/// Plays interface sounds through the OpenAL device <see cref="AudioPlayerService.AudioPlayerService"/>
/// opened. Silent until that one is initialized, and when it failed to.
/// </summary>
public sealed unsafe class OpenALSoundPlayer : ISoundPlayer, IDisposable
{
    // How many sounds overlap; one more cuts the one that started first
    private const int MaxSources = 8;

    private readonly AudioPlayerService.AudioPlayerService _device;

    private readonly Lock _lock = new();
    private readonly Dictionary<SoundClip, uint> _buffers = new();
    private readonly List<uint> _sources = new();
    private int _nextSource;

    public OpenALSoundPlayer(AudioPlayerService.AudioPlayerService device)
    {
        _device = device;
    }

    public void Play(SoundClip clip, float volume)
    {
        if (_device.Api is not { } al)
            return;

        lock (_lock)
        {
            if (!_buffers.TryGetValue(clip, out var buffer))
            {
                buffer = al.GenBuffer();
                fixed (byte* pcm = clip.Pcm)
                {
                    al.BufferData(buffer, clip.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16,
                        pcm, clip.Pcm.Length, clip.SampleRate);
                }

                _buffers.Add(clip, buffer);
            }

            var source = TakeSource(al);
            al.SourceStop(source);
            al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
            al.SetSourceProperty(source, SourceFloat.Gain, volume);
            al.SourcePlay(source);
        }
    }

    public void Release(SoundClip clip)
    {
        if (_device.Api is not { } al)
            return;

        lock (_lock)
        {
            if (!_buffers.Remove(clip, out var buffer))
                return;

            // A buffer cannot be deleted while a source holds it
            foreach (var source in _sources)
            {
                al.GetSourceProperty(source, GetSourceInteger.Buffer, out int attached);
                if (attached != (int)buffer)
                    continue;

                al.SourceStop(source);
                al.SetSourceProperty(source, SourceInteger.Buffer, 0);
            }

            al.DeleteBuffer(buffer);
        }
    }

    private uint TakeSource(AL al)
    {
        foreach (var source in _sources)
        {
            al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing)
                return source;
        }

        if (_sources.Count < MaxSources)
        {
            var source = al.GenSource();
            _sources.Add(source);
            return source;
        }

        _nextSource = (_nextSource + 1) % _sources.Count;
        return _sources[_nextSource];
    }

    public void Dispose()
    {
        if (_device.Api is not { } al)
            return;

        lock (_lock)
        {
            foreach (var source in _sources)
            {
                al.SourceStop(source);
                al.DeleteSource(source);
            }
            _sources.Clear();

            foreach (var buffer in _buffers.Values)
                al.DeleteBuffer(buffer);
            _buffers.Clear();
        }
    }
}
