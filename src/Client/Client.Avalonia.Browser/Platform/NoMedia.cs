using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Sounds;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Services;
using Client.ViewModels.Sessions.Chat;

namespace Client.Browser.Platform;

// What the desktop gets from Client.GStreamer, Client.LibWebRTC and Client.OpenAL. The browser has none of
// them: the media runtime is off (see Program), so these are only asked for, never used for real

internal sealed class NoMediaRuntimeLoader : IMediaRuntimeLoader
{
    public bool Load() => true;
    public bool UsesBundledRuntime => true;
}

internal sealed class NoLibWebRTCLoader : ILibWebRTCLoader
{
    public string? Load() => "There is no voice in the browser";
}

internal sealed class NoWebRTCTransportFactory : IWebRTCTransportFactory
{
    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true) =>
        throw new NotSupportedException("There is no voice in the browser");
}

internal sealed class NoMediaCapabilities : IMediaCapabilities
{
    public bool HasHardwareScreenShareEncoder => false;
    public IReadOnlyList<string> ScreenShareDecoders => [];
    public IReadOnlyList<string> ScreenShareAv1Decoders => [];
    public IReadOnlyList<string> ScreenShareEncoders => [];
    public IReadOnlyList<string> ScreenShareAv1Encoders => [];
    public bool CanCaptureScreenAudio => false;
}

internal sealed class NoAudioDeviceProvider : IAudioDeviceProvider
{
    public event Action? InputDevicesChanged { add { } remove { } }
    public event Action? OutputDevicesChanged { add { } remove { } }
    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => [];
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => [];
    public void SetOutputDevice(string? id, string? name) { }
    public void SetOutputVolume(double volume) { }
}

internal sealed class SilentAudioPlayerService : IAudioPlayerService
{
    public void Init() { }
    public void AddChannel(string id) { }
    public void RemoveChannel(string id) { }
    public void PlayPcm(byte[] pcmData) { }
    public void PlayPcm(string id, byte[] pcmData) { }
    public void ChangeVolume(float newVolume) { }
    public void ChangeVolume(string id, float newVolume) { }
    public void Stop() { }
    public void Dispose() { }
}

internal sealed class SilentSoundPlayer : ISoundPlayer
{
    public void Play(SoundClip clip, float volume) { }
    public void Release(SoundClip clip) { }
    public void Stop(SoundClip clip) { }
}

internal sealed class NoMicrophoneTester : IMicrophoneTester
{
    public event Action<double>? LevelChanged { add { } remove { } }
    public event Action<string>? Failed { add { } remove { } }
    public event Action<bool>? GateChanged { add { } remove { } }
    public bool IsRunning => false;

    public MicrophoneTestError? Start(string? deviceId, double volume, bool isLoopbackEnabled,
        NoiseSuppressionLevel noiseSuppression, VoiceGateOptions? voiceGate, bool isEchoCancellationEnabled,
        bool isAutoGainControlEnabled) => MicrophoneTestError.Unexpected;

    public void Stop() { }
    public void SetVolume(double volume) { }
    public void SetLoopbackEnabled(bool isEnabled) { }
    public void SetVoiceGate(VoiceGateOptions? voiceGate) { }
    public void Dispose() { }
}

/// <summary>Plays no audio or video attachments: <see cref="ChatMediaPlayback"/> refuses them with the media off.</summary>
internal sealed class NoChatMediaSession : IChatAudioSession, IChatVideoSession
{
    public event Action? Changed { add { } remove { } }
    public event Action<RawVideoFrame>? FrameReady { add { } remove { } }
    public double Volume => 1;
    public bool IsLoading => false;
    public bool IsPlaying => false;
    public bool HasError => false;
    public double Position => 0;
    public double Duration => 0;
    public bool IsBuffering => false;
    public double? StreamProgress => null;
    public Task ToggleAsync(ChatFileVM file) => Task.CompletedTask;
    public Task SetVolumeAsync(double volume) => Task.CompletedTask;
    public void Seek(ChatFileVM file, double position) { }
    public void Stop() { }
    public bool IsCurrent(ChatFileVM? file) => false;
    public Task InitializeAsync() => Task.CompletedTask;
    public void RedisplayFrame() { }
}

/// <summary>The browser says nothing of the network to .NET: always online, connections find out themselves.</summary>
internal sealed class BrowserConnectivityMonitor : IConnectivityMonitor
{
    public bool IsNetworkAvailable => true;
    public event Action<bool>? Changed { add { } remove { } }
}
