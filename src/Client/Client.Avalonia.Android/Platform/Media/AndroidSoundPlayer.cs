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
    // Keyed by whether a call was on when the track was made, see CreateTrack
    private readonly Dictionary<(SoundClip Clip, bool IsInCall), AudioTrack> _tracks = new();

    public void Play(SoundClip clip, float volume)
    {
        var isInCall = IsInCall();

        lock (_lock)
        {
            if (!_tracks.TryGetValue((clip, isInCall), out var track))
            {
                track = CreateTrack(clip, isInCall);
                _tracks.Add((clip, isInCall), track);
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
            foreach (var isInCall in (bool[])[false, true])
            {
                if (!_tracks.Remove((clip, isInCall), out var track))
                    continue;

                track.Release();
                track.Dispose();
            }
        }
    }

    // A voice session puts the device into a call, see VoicePeer.setCommunicationMode
    private static bool IsInCall() =>
        global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.AudioService) is AudioManager
        {
            Mode: Mode.InCommunication
        };

    /// <param name="isInCall">
    /// Whether a call is on. A notification sound is then cut and ducked by the system, so the sound
    /// goes as a signal of the call instead, the way the tones of a call do.
    /// </param>
    private static AudioTrack CreateTrack(SoundClip clip, bool isInCall)
    {
        // As a notification the sound follows the notification volume of the system and is silent in
        // the silent mode. The volume of interface sounds, which sonification goes by, is not the one
        // the user sets on most devices
        var attributes = new AudioAttributes.Builder()
            .SetUsage(isInCall ? AudioUsageKind.VoiceCommunicationSignalling : AudioUsageKind.Notification)!
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
