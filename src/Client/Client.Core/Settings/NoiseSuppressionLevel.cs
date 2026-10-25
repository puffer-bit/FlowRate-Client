namespace Client.Core.Settings;

/// <summary>
/// How much of the background noise is taken out of the microphone. Stronger levels remove more
/// of it and distort the voice more. The numbers are stored in the settings, so new levels are
/// only ever appended.
/// </summary>
public enum NoiseSuppressionLevel
{
    Off = 0,
    Low = 1,
    Moderate = 2,
    High = 3,
    VeryHigh = 4,

    /// <summary>
    /// The neural network of RNNoise takes the noise out instead of webrtcdsp, which still filters
    /// the rumble below the voice and sets the gain. It removes noise that is not steady, such as
    /// keys and clicks, far better than the levels above.
    /// </summary>
    Neural = 5,

    /// <summary>
    /// RNNoise first, then webrtcdsp at the moderate level for the steady noise it leaves.
    /// </summary>
    NeuralAndWebRtc = 6
}
