using System.Runtime.InteropServices;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.LibWebRTC.Voice;
using Shared.Enums.WebRTC;

namespace Client.LibWebRTC.Native;

/// <summary>
/// The desktop peer over flowrate_webrtc. GStreamer captures and processes its microphone; decoded
/// playback is pulled through the runtime's external audio device. Screens use encoded sources.
/// </summary>
internal sealed unsafe class NativeVoicePeer : IVoicePeer
{
    private readonly LibWebRTCRuntime _runtime;
    private readonly IVoicePeerListener _listener;
    private readonly Lock _lock = new();
    private readonly GCHandle _self;
    private readonly nint _peer;

    // The calls into the peer that run now, outside the lock: one of them may wait for the
    // signaling thread, whose callback may wait for a lock of the transport. The peer is destroyed
    // once the last of them returns
    private int _calls;
    private volatile bool _isClosed;

    private readonly Lock _mediaLock = new();
    private IProcessedMicrophone? _microphone;
    private (AudioInput Input, NoiseSuppressionLevel Suppression, bool Echo, bool Gain)? _settings;
    private VoiceGateOptions? _voiceGate;
    private double _volume = 1;
    private long _gateSummaryTaken = Environment.TickCount64;

    public NativeVoicePeer(LibWebRTCRuntime runtime, IVoicePeerListener listener)
    {
        _runtime = runtime;
        _listener = listener;
        _self = GCHandle.Alloc(this);

        var callbacks = new NativeMethods.PeerCallbacks
        {
            State = &OnState,
            Negotiated = &OnNegotiated,
            Failed = &OnFailed,
            Stats = &OnStats
        };

        // libwebrtc copies the callbacks, so they may live on the stack
        _peer = runtime.CreatePeer(&callbacks, GCHandle.ToIntPtr(_self), 0, 0, 0);

        if (_peer == 0)
        {
            _self.Free();
            throw new InvalidOperationException("libwebrtc did not create a peer connection");
        }
        runtime.Audio.Failed += OnAudioFailed;
    }

    private void OnAudioFailed(string error)
    {
        if (_isClosed)
            return;
        _listener.OnFailed(error);
        _listener.OnConnectionChanged(WebRTCConnectionState.Failed);
    }

    public void Negotiate(string offer, IReadOnlyList<SendSection> sends)
    {
        var mids = sends.Select(send => Marshal.StringToCoTaskMemUTF8(send.Mid)).ToArray();
        var videoSources = sends.Select(send => send.Video as NativeVideoSource).ToArray();
        var audioSources = sends.Select(send => send.Audio as NativeAudioSource).ToArray();

        // A source disposed in the meantime leaves its section empty until the next round
        var videos = videoSources.Select(source => source?.Acquire() ?? 0).ToArray();
        var audios = audioSources.Select(source => source?.Acquire() ?? 0).ToArray();

        try
        {
            var sections = new NativeMethods.SendSection[sends.Count];

            for (var index = 0; index < sections.Length; index++)
            {
                sections[index] = new NativeMethods.SendSection
                {
                    Mid = (byte*)mids[index],
                    Ssrc = sends[index].Ssrc,
                    RtxSsrc = sends[index].RtxSsrc,
                    Video = videos[index],
                    Audio = audios[index]
                };
            }

            fixed (NativeMethods.SendSection* sectionPointers = sections)
            {
                var sectionArray = sectionPointers;
                Run(peer => NativeMethods.PeerNegotiate(peer, offer, sectionArray, sections.Length));
            }
        }
        finally
        {
            foreach (var mid in mids)
                Marshal.FreeCoTaskMem(mid);

            for (var index = 0; index < sends.Count; index++)
            {
                if (videos[index] != 0)
                    videoSources[index]!.Release();

                if (audios[index] != 0)
                    audioSources[index]!.Release();
            }
        }
    }

    public IVideoSendSource? CreateVideoSource(int width, int height, int maxFramerate, IVideoSendListener listener) =>
        NativeVideoSource.Create(width, height, maxFramerate, listener);

    public IAudioSendSource? CreateAudioSource() => NativeAudioSource.Create();

    public void SetMicrophoneEnabled(bool isEnabled) => Run(peer => NativeMethods.PeerSetMicrophoneEnabled(peer, isEnabled ? 1 : 0));

    public bool SetMicrophone(AudioInput input, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled,
        bool isAutoGainControlEnabled)
    {
        lock (_mediaLock)
        {
            if (_isClosed)
                return false;
            var settings = (input, noiseSuppression, isEchoCancellationEnabled, isAutoGainControlEnabled);
            if (_settings == settings && _microphone != null)
                return true;
            var microphone = _runtime.Audio.CreateMicrophone(input, _volume, noiseSuppression, _voiceGate,
                isEchoCancellationEnabled, isAutoGainControlEnabled);
            if (microphone == null)
                return false;
            var previous = _microphone;
            if (previous != null)
                previous.SamplesReady -= Capture;
            microphone.SamplesReady += Capture;
            if (!microphone.Start())
            {
                microphone.SamplesReady -= Capture;
                microphone.Dispose();
                if (previous != null)
                    previous.SamplesReady += Capture;
                return false;
            }
            Volatile.Write(ref _microphone, microphone);
            _settings = settings;
            previous?.Dispose();
            return true;
        }
    }

    public void SetMicrophoneVolume(double volume)
    {
        lock (_mediaLock)
        {
            _volume = volume;
            _microphone?.SetVolume(volume);
        }
    }

    public void StopMicrophone()
    {
        SetMicrophoneEnabled(false);
        lock (_mediaLock)
        {
            ReleaseMicrophone();
            _settings = null;
            _voiceGate = null;
        }
    }

    // Under _mediaLock. The reader is stopped outside the native-call lock so its last frame
    // can finish while capture ownership returns to another active peer.
    private void ReleaseMicrophone()
    {
        var microphone = Interlocked.Exchange(ref _microphone, null);
        if (microphone == null)
            return;
        microphone.SamplesReady -= Capture;
        microphone.Dispose();
    }

    private void Capture(ReadOnlySpan<short> frame)
    {
        var length = frame.Length;
        fixed (short* samples = frame)
        {
            var data = (nint)samples;
            Run(peer => NativeMethods.PeerCapture(peer, (short*)data, length, 48000, 1));
        }
    }

    public void SetVoiceGate(VoiceGateOptions? voiceGate)
    {
        lock (_mediaLock)
        {
            _voiceGate = voiceGate;
            _microphone?.SetVoiceGate(voiceGate);
        }
    }

    public void SetVolume(string mid, double volume) => Run(peer => NativeMethods.PeerSetVolume(peer, mid, volume));

    // flowrate_webrtc reads it all every time
    public void RequestStats(bool isDetailed) => Run(NativeMethods.PeerRequestStats);

    /// <summary>
    /// Silences the callbacks at once and destroys the peer in the background: it may be called on
    /// a thread of libwebrtc, and destroying waits for them.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isClosed)
                return;
            _isClosed = true;
        }
        _runtime.Audio.Failed -= OnAudioFailed;
        lock (_mediaLock)
            ReleaseMicrophone();

        Task.Run(() =>
        {
            SpinWait.SpinUntil(() => Volatile.Read(ref _calls) == 0);

            // No callback comes once it returns, so the handle is free to go
            _runtime.DestroyPeer(_peer);
            _self.Free();
        });
    }

    private void Run(Action<nint> call)
    {
        lock (_lock)
        {
            if (_isClosed)
                return;

            Interlocked.Increment(ref _calls);
        }

        try
        {
            call(_peer);
        }
        finally
        {
            Interlocked.Decrement(ref _calls);
        }
    }

    private static IVoicePeerListener? ListenerOf(nint context) =>
        GCHandle.FromIntPtr(context).Target is NativeVoicePeer { _isClosed: false } peer ? peer._listener : null;

    // The callbacks come on the threads of libwebrtc, where an exception may not go; the listener
    // catches its own

    [UnmanagedCallersOnly]
    private static void OnState(nint context, int state) =>
        ListenerOf(context)?.OnConnectionChanged((WebRTCConnectionState)state);

    [UnmanagedCallersOnly]
    private static void OnNegotiated(nint context, byte* localSdp) =>
        ListenerOf(context)?.OnNegotiated(Marshal.PtrToStringUTF8((nint)localSdp) ?? string.Empty);

    [UnmanagedCallersOnly]
    private static void OnFailed(nint context, byte* error) =>
        ListenerOf(context)?.OnFailed(Marshal.PtrToStringUTF8((nint)error) ?? "unknown");

    [UnmanagedCallersOnly]
    private static void OnStats(nint context, byte* json)
    {
        try
        {
            var text = Marshal.PtrToStringUTF8((nint)json) ?? "[]";

            if (GCHandle.FromIntPtr(context).Target is NativeVoicePeer { _isClosed: false } peer)
            {
                var microphone = Volatile.Read(ref peer._microphone);
                var stats = StatsReader.Read(text);
                var levels = stats.Levels.Where(level => level.Mid != null)
                    .Append(new VoiceLevel(null, microphone?.TakeLevel() ?? 0)).ToArray();
                Client.Core.Media.VoiceGateSummary? summary = null;
                var now = Environment.TickCount64;
                if (now - peer._gateSummaryTaken >= 60_000)
                {
                    peer._gateSummaryTaken = now;
                    summary = microphone?.TakeGateSummary();
                }
                peer._listener.OnStats(stats with { Levels = levels, GateSummary = summary });
            }
        }
        catch
        {
            // A report that does not read is skipped; the next one comes in 200 ms
        }
    }
}
