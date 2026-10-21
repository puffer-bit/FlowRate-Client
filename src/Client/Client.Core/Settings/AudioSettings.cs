namespace Client.Core.Settings;

public class AudioSettings
{
    public const double MinInputVolume = 0.0;
    public const double MaxInputVolume = 2.0;

    public const double MinSoundsVolume = 0.0;
    public const double MaxSoundsVolume = 1.0;

    // The range of the microphone test meter, so the threshold can be set against it
    public const double MinVoiceGateThresholdDb = -60.0;
    public const double MaxVoiceGateThresholdDb = 0.0;
    
    /// <summary>
    /// Stable id of the chosen input device, or null for the system default one.
    /// </summary>
    public string? InputDeviceId { get; set; }
    
    /// <summary>
    /// Name of the chosen device, used to find it again when the backend changes its id.
    /// </summary>
    public string? InputDeviceName { get; set; }
    
    /// <summary>
    /// Gain applied to the input, 1.0 keeps the level unchanged.
    /// </summary>
    public double InputVolume { get; set; } = 1.0;

    /// <summary>
    /// Whether the microphone is sent only while the user speaks. Off sends it all the time.
    /// </summary>
    public bool IsVoiceGateEnabled { get; set; } = true;

    /// <summary>
    /// Peak level in dBFS, after the input volume, from which the voice gate opens.
    /// </summary>
    public double VoiceGateThresholdDb { get; set; } = -32.0;

    /// <summary>
    /// How much background noise is taken out of the microphone.
    /// </summary>
    public NoiseSuppressionLevel NoiseSuppression { get; set; } = NoiseSuppressionLevel.Moderate;

    /// <summary>
    /// Whether the interface makes sounds: someone joining, the microphone being muted and the like.
    /// </summary>
    public bool AreSoundsEnabled { get; set; } = true;

    /// <summary>
    /// Volume of the interface sounds, 1.0 plays them as they are.
    /// </summary>
    public double SoundsVolume { get; set; } = 0.7;

    /// <summary>
    /// Name of the sound pack that replaces the interface sounds, or null for the built-in ones.
    /// </summary>
    public string? SoundPack { get; set; }

    /// <summary>
    /// The interface sounds that are switched off one by one, by the name of their file without
    /// the extension ("join", "message_sent").
    /// </summary>
    public string[] DisabledSounds { get; set; } = [];

    /// <summary>
    /// The threshold the voice gate works with, or null when it is off and the microphone is sent all the time.
    /// </summary>
    public double? ActiveVoiceGateThresholdDb => IsVoiceGateEnabled ? VoiceGateThresholdDb : null;
}
