namespace Client.Core.Settings;

/// <summary>
/// How much of the background noise is taken out of the microphone. Stronger levels remove more
/// of it and distort the voice more. The numbers are stored in the settings.
/// </summary>
public enum NoiseSuppressionLevel
{
    Off = 0,
    Low = 1,
    Moderate = 2,
    High = 3,
    VeryHigh = 4
}
