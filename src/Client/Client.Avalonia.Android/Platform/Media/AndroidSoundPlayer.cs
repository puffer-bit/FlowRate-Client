using System.Collections.Generic;
using Android.Media;
using Client.Application.Ports;
using Client.Application.Sounds;

namespace Client.Droid.Platform.Media;

/// <summary>
/// Plays interface sounds, each from a track that holds the whole clip.
/// </summary>
internal sealed class AndroidSoundPlayer : ISoundPlayer
{
    private readonly System.Threading.Lock _lock = new();
    private readonly Dictionary<SoundClip, AudioTrack> _tracks = new();

    public void Play(SoundClip clip, float volume)
    {
        lock (_lock)
        {
            if (!_tracks.TryGetValue(clip, out var track))
            {
                track = CreateTrack(clip);
                _tracks.Add(clip, track);
            }
            else
            {
                // A static track stays at its end after it played; it has to be stopped to rewind
                track.Stop();
                track.ReloadStaticData();
            }

            track.SetVolume(volume);
            track.Play();
        }
    }

    public void Release(SoundClip clip)
    {
        lock (_lock)
        {
            if (!_tracks.Remove(clip, out var track))
                return;

            track.Release();
            track.Dispose();
        }
    }

    private static AudioTrack CreateTrack(SoundClip clip)
    {
        // As a notification the sound follows the notification volume of the system and is silent in
        // the silent mode. The volume of interface sounds, which sonification goes by, is not the one
        // the user sets on most devices
        var attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Notification)!
            .SetContentType(AudioContentType.Sonification)!
            .Build()!;
        var format = new AudioFormat.Builder()
            .SetEncoding(Encoding.Pcm16bit)!
            .SetSampleRate(clip.SampleRate)!
            .SetChannelMask(clip.Channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo)!
            .Build()!;

        var track = new AudioTrack.Builder()
            .SetAudioAttributes(attributes)
            .SetAudioFormat(format)
            .SetBufferSizeInBytes(clip.Pcm.Length)
            .SetTransferMode(AudioTrackMode.Static)
            .Build();
        track.Write(clip.Pcm, 0, clip.Pcm.Length);

        return track;
    }
}
