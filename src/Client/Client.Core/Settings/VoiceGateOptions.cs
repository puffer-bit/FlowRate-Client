namespace Client.Core.Settings;

/// <summary>
/// How the voice gate of a microphone decides. Where these are passed, null turns the gate off and
/// sends the microphone all the time.
/// </summary>
/// <param name="ThresholdDb">Peak level in dBFS, after the input volume, from which the gate opens.</param>
/// <param name="IsVoiceDetectionEnabled">
/// The gate opens only for what RNNoise takes for a voice, so keys, clicks and knocks loud enough
/// for the threshold stay out. Without RNNoise in the runtime the gate goes by the level alone.
/// </param>
/// <param name="IsAdaptiveThresholdEnabled">
/// The gate follows the noise of the room while it is closed and opens only clearly above it, never
/// below <paramref name="ThresholdDb"/>.
/// </param>
public sealed record VoiceGateOptions(double ThresholdDb, bool IsVoiceDetectionEnabled = false,
    bool IsAdaptiveThresholdEnabled = false);
