using Client.Application.Sounds;

namespace Client.Application.Ports;

/// <summary>
/// The audio output of the platform for short interface sounds.
/// </summary>
public interface ISoundPlayer
{
    /// <summary>
    /// Starts the clip and returns at once. Clips overlap; a clip that is started again while it
    /// still sounds may be cut and restarted.
    /// </summary>
    /// <param name="volume">From 0.0 to 1.0.</param>
    void Play(SoundClip clip, float volume);

    /// <summary>
    /// Frees what was prepared for a clip that will not be played again.
    /// </summary>
    void Release(SoundClip clip);
}
