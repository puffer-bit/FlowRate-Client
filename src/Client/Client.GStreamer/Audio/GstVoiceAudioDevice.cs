using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Settings;
using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Audio;

/// <summary>
/// One hardware clock, playback mix and processed microphone for all native peers. The echo probe
/// and microphone DSP live in the same top-level pipeline, as webrtcdsp requires.
/// </summary>
public sealed class GstVoiceAudioDevice(GstAudioDeviceProvider devices, ILogger<GstVoiceAudioDevice> logger)
    : IVoiceAudioDevice, IDisposable
{
    private readonly Lock _lock = new();
    private Pipeline? _pipeline;
    private Element? _playout;
    private GstBusWatcher? _bus;
    private string? _echoProbe;
    private GstProcessedMicrophone? _capture;
    private Microphone[] _owners = [];
    private ulong _nextTimestamp;
    private static int _probeCount;

    // Integration checks use a clocked fakesink without opening the user's speakers.
    internal string? PlaybackSink { get; init; }
    internal bool IsRunning { get { lock (_lock) return _pipeline != null; } }

    public event Action<string>? Failed;

    public bool StartPlayback(AudioPlayoutHandler read)
    {
        lock (_lock)
        {
            if (_pipeline != null)
                return true;
            try
            {
                _echoProbe = ElementFactory.Find(MicrophoneProcessing.EchoProbeElementName) != null
                    ? $"native_echo_{Interlocked.Increment(ref _probeCount)}" : null;
                var probe = _echoProbe == null ? "" :
                    $"audioconvert ! {MicrophoneProcessing.EchoProbeCaps} ! " +
                    $"{MicrophoneProcessing.EchoProbeElementName} name={_echoProbe} ! ";
                _pipeline = (Pipeline)Global.ParseLaunch(
                    "appsrc name=playout is-live=true format=time block=false max-buffers=2 max-bytes=0 " +
                    "max-time=20000000 leaky-type=downstream " +
                    "caps=\"audio/x-raw,format=S16LE,rate=48000,channels=2,layout=interleaved\" ! " +
                    "clocksync name=playout_clock sync=true sync-to-first=false ! " +
                    "audioconvert ! " + VoicePlaybackSink.ShapingDescription + probe +
                    "audioconvert ! audioresample ! " + (PlaybackSink ?? VoicePlaybackSink.Description));
                _playout = _pipeline.GetByName("playout") ?? throw new InvalidOperationException("Missing playout source");
                var pipeline = _pipeline;
                var playout = _playout;
                var pcm = new short[960];
                ulong? timingStarted = null;
                long maximumLead = 0;
                var framesPushed = 0;
                _nextTimestamp = 0;
                // need-data means queue space, not a hardware tick. clocksync blocks this
                // streaming thread until the buffer's PTS before allowing the next pull.
                // No queue may be inserted between appsrc and clocksync: it would allow
                // NetEq to be read ahead into that queue instead of pacing each 10 ms frame.
                playout.ConnectSignal("need-data", (_, _) =>
                {
                    try
                    {
                        var now = GstPcmNative.RunningTime(pipeline);
                        if (now > _nextTimestamp + 50_000_000)
                        {
                            // Bound the work on the streaming thread after a long suspension.
                            var skipped = (int)Math.Min((now - _nextTimestamp) / 10_000_000, 200UL);
                            for (var frame = 0; frame < skipped; frame++)
                                read(pcm);
                            logger.LogWarning("Audio output resumed {DelayMs:F0} ms late; discarded {Frames} stale playout frames",
                                (now - _nextTimestamp) / 1e6, skipped);
                            _nextTimestamp = now;
                        }
                        read(pcm);
                        GstPcmNative.Push(playout, pcm, _nextTimestamp);
                        var lead = (long)_nextTimestamp - (long)now;
                        maximumLead = Math.Max(maximumLead, lead);
                        framesPushed++;
                        timingStarted ??= now;
                        if (now - timingStarted.Value >= 5_000_000_000)
                        {
                            logger.LogDebug("Playout pacing: {FramesPerSecond:F1} frames/s, clock lead {LeadMs:F1} ms, maximum {MaxLeadMs:F1} ms",
                                framesPushed * 1e9 / (now - timingStarted.Value), lead / 1e6, maximumLead / 1e6);
                            timingStarted = now;
                            framesPushed = 0;
                            maximumLead = 0;
                        }
                        _nextTimestamp += 10_000_000;
                    }
                    catch (Exception error)
                    {
                        logger.LogError(error, "Failed to pull native playout PCM");
                        Array.Clear(pcm);
                        GstPcmNative.Push(playout, pcm, _nextTimestamp);
                        _nextTimestamp += 10_000_000;
                    }
                    return null;
                });
                VoicePlaybackSink.SetMuted(_pipeline, false);
                _bus = new GstBusWatcher(_pipeline.GetBus(),
                    MessageType.Element | MessageType.Error | MessageType.Warning | MessageType.Latency,
                    OnMessage, "native audio", logger);
                if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                    throw new InvalidOperationException("Cannot start native audio playback");
                return true;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Cannot start GStreamer audio for libwebrtc");
                StopPlayback();
                return false;
            }
        }
    }

    private void OnMessage(Message message)
    {
        if (message.Type == MessageType.Latency)
        {
            // There is no GLib main loop to handle latency changes when a microphone is added
            // or its gate changes. Keep the output and the echo probe on the same latency.
            var pipeline = Volatile.Read(ref _pipeline);
            if (pipeline != null)
            {
                pipeline.RecalculateLatency();
                using var output = pipeline.GetByName(VoicePlaybackSink.ElementName);
                using var factory = output?.GetFactory();
                logger.LogDebug("Audio output latency {LatencyMs} ms (sink: {Sink})",
                    pipeline.GetConfiguredLatency().TotalMilliseconds, factory?.GetName());
            }
        }
        else if (message.Type == MessageType.Element)
            Volatile.Read(ref _capture)?.OnMessage(message);
        else
        {
            var (error, debug) = message.Type == MessageType.Error ? message.ParseError() : message.ParseWarning();
            logger.LogWarning("Native audio {Type}: {Message} ({Debug})", message.Type, error.Message, debug);
            var failedPipeline = Volatile.Read(ref _pipeline);
            if (message.Type == MessageType.Error)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    // Subscribers may close a peer and stop the pipeline. Never do that on the
                    // bus thread, whose shutdown waits for the current handler to return.
                    try
                    {
                        if (failedPipeline != null && ReferenceEquals(Volatile.Read(ref _pipeline), failedPipeline))
                            Failed?.Invoke(error.Message);
                    }
                    catch (Exception failure) { logger.LogWarning(failure, "Native audio failure handler failed"); }
                });
        }
    }

    public IProcessedMicrophone? CreateMicrophone(AudioInput input, double volume, NoiseSuppressionLevel suppression,
        VoiceGateOptions? gate, bool echoCancellation, bool autoGainControl) =>
        input is DeviceAudioInput or ToneAudioInput or FileAudioInput
            ? new Microphone(this, new Options(input, volume, suppression, gate, echoCancellation, autoGainControl))
            : null;

    private sealed record Options(AudioInput Input, double Volume, NoiseSuppressionLevel Suppression,
        VoiceGateOptions? Gate, bool EchoCancellation, bool AutoGainControl);

    private bool Activate(Microphone owner)
    {
        lock (_lock)
        {
            if (_pipeline == null)
                return false;
            var previous = _owners;
            _owners = [.. previous.Where(item => item != owner), owner];
            if ((_capture != null && previous.LastOrDefault()?.Settings == owner.Settings) || Rebuild(owner.Settings))
                return true;
            _owners = previous;
            if (previous.Length > 0)
                Rebuild(previous[^1].Settings);
            return false;
        }
    }

    private bool Rebuild(Options options)
    {
        var previous = Interlocked.Exchange(ref _capture, null);
        previous?.Dispose();
        try
        {
            var source = options.Input is DeviceAudioInput device
                ? devices.CreateInputElement(device.DeviceId) : FileAudioSource.CreateElement(options.Input);
            if (source == null || _pipeline == null)
            {
                source?.Dispose();
                return false;
            }
            var capture = new GstProcessedMicrophone(_pipeline, source, options.Volume, options.Suppression,
                options.Gate, options.EchoCancellation ? _echoProbe : null, options.AutoGainControl, logger);
            capture.SamplesReady += ForwardSamples;
            Volatile.Write(ref _capture, capture);
            if (capture.Start())
                return true;
            Interlocked.Exchange(ref _capture, null)?.Dispose();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Cannot start processed microphone for libwebrtc");
        }
        return false;
    }

    private void ForwardSamples(ReadOnlySpan<short> samples)
    {
        foreach (var owner in Volatile.Read(ref _owners))
            owner.Deliver(samples);
    }

    private void Release(Microphone owner)
    {
        lock (_lock)
        {
            var wasCurrent = _owners.LastOrDefault() == owner;
            _owners = _owners.Where(item => item != owner).ToArray();
            if (_owners.Length == 0)
                Interlocked.Exchange(ref _capture, null)?.Dispose();
            else if (wasCurrent && owner.Settings != _owners[^1].Settings)
                Rebuild(_owners[^1].Settings);
        }
    }

    public void StopPlayback()
    {
        lock (_lock)
        {
            _owners = [];
            Interlocked.Exchange(ref _capture, null)?.Dispose();
            // Bus handlers never take this lock, and stop before their element wrappers are freed.
            _bus?.Dispose();
            _bus = null;
            _pipeline?.SetState(State.Null);
            _playout?.Dispose();
            _playout = null;
            _pipeline?.Dispose();
            _pipeline = null;
            _echoProbe = null;
        }
    }

    public void Dispose() => StopPlayback();

    private sealed class Microphone(GstVoiceAudioDevice device, Options options) : IProcessedMicrophone
    {
        public Options Settings { get; private set; } = options;
        private int _peak;
        private volatile bool _disposed;
        public event AudioSamplesHandler? SamplesReady;
        public bool Start() => !_disposed && device.Activate(this);
        public void Deliver(ReadOnlySpan<short> samples)
        {
            if (_disposed)
                return;
            var peak = 0;
            foreach (var sample in samples)
                peak = Math.Max(peak, Math.Abs((int)sample));
            int previous;
            do { previous = Volatile.Read(ref _peak); }
            while (peak > previous && Interlocked.CompareExchange(ref _peak, peak, previous) != previous);
            SamplesReady?.Invoke(samples);
        }
        public double TakeLevel() => Math.Min(1, Interlocked.Exchange(ref _peak, 0) / 32768.0);
        public VoiceGateSummary? TakeGateSummary() => Volatile.Read(ref device._capture)?.TakeGateSummary();
        public void SetVolume(double volume)
        {
            lock (device._lock)
            {
                Settings = Settings with { Volume = volume };
                if (device._owners.LastOrDefault() == this)
                    device._capture?.SetVolume(volume);
            }
        }
        public void SetVoiceGate(VoiceGateOptions? gate)
        {
            lock (device._lock)
            {
                Settings = Settings with { Gate = gate };
                if (device._owners.LastOrDefault() == this)
                    device._capture?.SetVoiceGate(gate);
            }
        }
        public void Dispose()
        {
            _disposed = true;
            device.Release(this);
        }
    }
}
