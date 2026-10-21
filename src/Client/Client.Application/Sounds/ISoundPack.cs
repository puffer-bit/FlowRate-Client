namespace Client.Application.Sounds;

/// <summary>
/// A set of sounds. A pack may leave events out: the built-in sound is played for them.
/// </summary>
public interface ISoundPack
{
    /// <returns>The WAV file of the event, or null when the pack has none.</returns>
    Stream? Open(SoundEvent soundEvent);
}
