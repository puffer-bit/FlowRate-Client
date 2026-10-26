using System;
using Client.Application.Ports;
using Client.Core.Settings;

namespace Client.Droid.Platform.Media;

/// <summary>
/// Never starts: the microphone test is not on Android yet, and its settings are not shown there.
/// </summary>
internal sealed class NoMicrophoneTester : IMicrophoneTester
{
    public event Action<double>? LevelChanged
    {
        add { }
        remove { }
    }

    public event Action<string>? Failed
    {
        add { }
        remove { }
    }

    public event Action<bool>? GateChanged
    {
        add { }
        remove { }
    }

    public bool IsRunning => false;

    public MicrophoneTestError? Start(string? deviceId, double volume, bool isLoopbackEnabled,
        NoiseSuppressionLevel noiseSuppression, VoiceGateOptions? voiceGate, bool isEchoCancellationEnabled) =>
        MicrophoneTestError.Unexpected;

    public void Stop()
    {
    }

    public void SetVolume(double volume)
    {
    }

    public void SetLoopbackEnabled(bool isEnabled)
    {
    }

    public void SetVoiceGate(VoiceGateOptions? voiceGate)
    {
    }

    public void Dispose()
    {
    }
}
