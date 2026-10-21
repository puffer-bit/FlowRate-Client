using Client.Core.Settings;

namespace Client.Application.Sounds;

public interface ISoundService
{
    /// <summary>
    /// Off plays nothing.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// From 0.0 to 1.0.
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// Takes <see cref="IsEnabled"/>, <see cref="Volume"/>, the sound pack and the sounds that are
    /// switched off from the settings.
    /// </summary>
    void Apply(AudioSettings settings);

    /// <summary>
    /// Plays the sound of the event over whatever else is playing, unless it is switched off. Never throws and may be called
    /// from any thread.
    /// </summary>
    void Play(SoundEvent soundEvent);

    /// <summary>
    /// Plays the sound for the user to hear what it is: also when the sounds, or this one, are
    /// switched off.
    /// </summary>
    void Preview(SoundEvent soundEvent);

    /// <summary>
    /// Replaces the sounds with the ones of the pack, from the next play on. The events the pack
    /// leaves out, or has a broken file for, keep their built-in sound.
    /// </summary>
    /// <param name="pack">The pack, or null to return to the built-in sounds.</param>
    void UsePack(ISoundPack? pack);

    /// <summary>
    /// Replaces the sound of one event, over the pack in use.
    /// </summary>
    /// <param name="file">A WAV file, or null to return to the sound of the pack.</param>
    /// <returns>False when the file is not a sound that can be played; nothing changes then.</returns>
    bool Replace(SoundEvent soundEvent, Stream? file);
}
