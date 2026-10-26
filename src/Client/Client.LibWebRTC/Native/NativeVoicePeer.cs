using System.Runtime.InteropServices;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.LibWebRTC.Voice;
using Shared.Enums.WebRTC;

namespace Client.LibWebRTC.Native;

/// <summary>
/// The voice peer of the desktop, over flowrate_webrtc. The microphone and the speakers are the
/// devices of libwebrtc itself: PulseAudio on Linux, WASAPI on Windows. A tone or a file is played
/// into a custom source instead of the microphone.
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

    // What the track we send was created with, so the same options do not replace it
    private (bool IsCustom, bool EchoCancellation, bool NoiseSuppression) _options = (false, false, true);
    private CustomAudioFeeder? _feeder;
    private double _volume = 1;

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
        _peer = NativeMethods.PeerCreate(&callbacks, GCHandle.ToIntPtr(_self), 0, 1, 1);

        if (_peer == 0)
        {
            _self.Free();
            throw new InvalidOperationException("libwebrtc did not create a peer connection");
        }
    }

    public void Negotiate(string offer, IReadOnlyList<string> sendMids, IReadOnlyList<uint> sendSsrcs)
    {
        var mids = sendMids.Select(mid => Marshal.StringToCoTaskMemUTF8(mid)).ToArray();
        var ssrcs = sendSsrcs.ToArray();

        try
        {
            fixed (nint* midPointers = mids)
            fixed (uint* ssrcPointers = ssrcs)
            {
                var midArray = (byte**)midPointers;
                var ssrcArray = ssrcPointers;
                Run(peer => NativeMethods.PeerNegotiate(peer, offer, midArray, ssrcArray, mids.Length));
            }
        }
        finally
        {
            foreach (var mid in mids)
                Marshal.FreeCoTaskMem(mid);
        }
    }

    public void SetMicrophoneEnabled(bool isEnabled) => Run(peer => NativeMethods.PeerSetMicrophoneEnabled(peer, isEnabled ? 1 : 0));

    public bool SetMicrophone(AudioInput input, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled)
    {
        var feeder = input is DeviceAudioInput ? null : CustomAudioFeeder.For(input, Capture);

        // A device, a tone or a file: the sound of other applications is not captured here
        if (input is not DeviceAudioInput && feeder == null)
            return false;

        if (input is DeviceAudioInput device)
            _runtime.SelectRecordingDevice(device.DeviceId);

        // libwebrtc has one level of noise suppression of its own, taken for every level but off
        var options = (feeder != null, isEchoCancellationEnabled, noiseSuppression != NoiseSuppressionLevel.Off);
        CustomAudioFeeder? previous;
        bool isChanged;

        lock (_lock)
        {
            if (_isClosed)
            {
                feeder?.Dispose();
                return false;
            }

            previous = _feeder;
            _feeder = feeder;
            isChanged = options != _options;
            _options = options;

            if (feeder != null)
                feeder.Volume = _volume;
        }

        previous?.Dispose();

        if (isChanged)
            Run(peer => NativeMethods.PeerSetAudioOptions(peer, options.Item1 ? 1 : 0, options.isEchoCancellationEnabled ? 1 : 0,
                options.Item3 ? 1 : 0, 1));

        return true;
    }

    public void SetMicrophoneVolume(double volume)
    {
        NativeMethods.SetCaptureVolume(volume);

        // A custom source goes past the processing that applies it to the microphone
        lock (_lock)
        {
            _volume = volume;

            if (_feeder != null)
                _feeder.Volume = volume;
        }
    }

    private void Capture(short[] frame) => Run(peer =>
    {
        fixed (short* samples = frame)
            NativeMethods.PeerCapture(peer, samples, frame.Length, CustomAudioFeeder.SampleRate, 1);
    });

    public void SetVoiceGate(VoiceGateOptions? voiceGate) => NativeVoiceGate.Set(voiceGate);

    public void SetVolume(string mid, double volume) => Run(peer => NativeMethods.PeerSetVolume(peer, mid, volume));

    public void RequestStats() => Run(NativeMethods.PeerRequestStats);

    /// <summary>
    /// Silences the callbacks at once and destroys the peer in the background: it may be called on
    /// a thread of libwebrtc, and destroying waits for them.
    /// </summary>
    public void Dispose()
    {
        CustomAudioFeeder? feeder;

        lock (_lock)
        {
            if (_isClosed)
                return;

            _isClosed = true;
            feeder = _feeder;
            _feeder = null;
        }

        feeder?.Dispose();

        Task.Run(() =>
        {
            SpinWait.SpinUntil(() => Volatile.Read(ref _calls) == 0);

            // No callback comes once it returns, so the handle is free to go
            NativeMethods.PeerDestroy(_peer);
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
            if (ListenerOf(context) is { } listener)
                listener.OnStats(StatsReader.Read(Marshal.PtrToStringUTF8((nint)json) ?? "[]"));
        }
        catch
        {
            // A report that does not read is skipped; the next one comes in 200 ms
        }
    }
}
