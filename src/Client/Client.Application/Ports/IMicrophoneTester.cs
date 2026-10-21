using Client.Core.Settings;

namespace Client.Application.Ports;

/// <summary>
/// Why the microphone test did not start.
/// </summary>
public enum MicrophoneTestError
{
    DeviceNotConnected,
    DeviceLinkFailed,
    DeviceOpenFailed,
    Unexpected
}

/// <summary>
/// Captures from an input device outside of any session, so the user can see and hear that the
/// device actually works.
/// </summary>
public interface IMicrophoneTester : IDisposable
{
    /// <summary>
    /// Peak level of the last few dozen milliseconds, 0 for silence and 1 for full scale.
    /// Raised from a thread of the tester.
    /// </summary>
    event Action<double>? LevelChanged;
    
    /// <summary>
    /// Raised from a GStreamer thread when capturing stops because of an error. Carries the words of
    /// the audio backend, which are a technical detail and not a text of the interface.
    /// </summary>
    event Action<string>? Failed;
    
    bool IsRunning { get; }

    /// <param name="deviceId">Device to capture from, or null for the system default one.</param>
    /// <param name="volume">Gain applied to the input, 1.0 keeps the level unchanged.</param>
    /// <param name="isLoopbackEnabled">Whether the captured sound is played back to the default output.</param>
    /// <param name="noiseSuppression">How much background noise is taken out, as the voice pipeline would.</param>
    /// <returns>Null when capturing started.</returns>
    MicrophoneTestError? Start(string? deviceId, double volume, bool isLoopbackEnabled,
        NoiseSuppressionLevel noiseSuppression);
    void Stop();
    
    void SetVolume(double volume);
    void SetLoopbackEnabled(bool isEnabled);
}
