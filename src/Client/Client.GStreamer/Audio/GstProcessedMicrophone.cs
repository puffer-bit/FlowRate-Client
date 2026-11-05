using System.Runtime.InteropServices;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Settings;
using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Audio;

/// <summary>The same microphone processing as the test, ending in 10 ms PCM instead of an encoder.</summary>
internal sealed class GstProcessedMicrophone : IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly Bin _branch;
    private readonly List<Gst.GObject.Object> _wrappers = [];
    private readonly Lock _lock = new();
    private readonly VoiceGate? _gate;
    private readonly nint _levelHandle;
    private readonly nint _voiceHandle;
    private readonly Element? _volume;
    private readonly AppSinkReader _reader;
    private bool _disposed;

    public event AudioSamplesHandler? SamplesReady;

    public GstProcessedMicrophone(Pipeline pipeline, Element source, double volume,
        NoiseSuppressionLevel suppression, VoiceGateOptions? gate, string? echoProbe, bool autoGainControl,
        ILogger logger)
    {
        _pipeline = pipeline;
        _branch = Bin.New("native_microphone");
        try
        {
            // BuildMonitor takes ownership of the source, including on failure.
            var monitor = MicrophoneChain.BuildMonitor(source, volume, suppression, echoProbe, autoGainControl,
                "fakesink sync=false async=false") ?? throw new InvalidOperationException("Cannot build microphone processing");
            _wrappers.Add(monitor);
            var output = Global.ParseBinFromDescription(
                "audioconvert ! audioresample ! audio/x-raw,format=S16LE,rate=48000,channels=1,layout=interleaved ! " +
                "audiobuffersplit output-buffer-duration=1/100 ! " +
                "appsink name=pcm sync=false async=false max-buffers=2 drop=true", true)
                ?? throw new InvalidOperationException("Cannot build microphone PCM output");
            _wrappers.Add(output);
            _branch.Add(monitor);
            _branch.Add(output);
            if (!monitor.Link(output))
                throw new InvalidOperationException("Cannot link microphone PCM output");

            _volume = Track(monitor.GetByName(MicrophoneChain.InputVolumeName));
            var level = Track(monitor.GetByName(MicrophoneChain.GateLevelName));
            var valve = Track(monitor.GetByName(MicrophoneChain.GateValveName));
            var voice = Track(monitor.GetByName(MicrophoneChain.VoiceLevelName));
            var voiceValve = Track(monitor.GetByName(MicrophoneChain.VoiceValveName));
            if (level != null && valve != null)
            {
                var detector = voice != null && voiceValve != null ? new VoiceGate.VoiceDetector(voice, voiceValve) : null;
                _gate = new VoiceGate("native_microphone", level, valve, detector, gate);
                _levelHandle = level.Handle;
                _voiceHandle = voice?.Handle ?? 0;
            }

            pipeline.Add(_branch);
            var sink = output.GetByName("pcm") ?? throw new InvalidOperationException("Missing PCM sink");
            _reader = new AppSinkReader(sink, "native microphone", OnSample,
                error => logger.LogWarning(error, "Failed to deliver processed microphone PCM"));
        }
        catch
        {
            _branch.SetState(State.Null);
            pipeline.Remove(_branch);
            foreach (var wrapper in _wrappers)
                wrapper.Dispose();
            _branch.Dispose();
            throw;
        }
    }

    private Element? Track(Element? element)
    {
        if (element != null)
            _wrappers.Add(element);
        return element;
    }

    public bool Start() => _branch.SyncStateWithParent();

    private void OnSample(Sample sample)
    {
        using var buffer = sample.GetBuffer();
        if (buffer == null)
            return;
        using var map = buffer.Map(MapFlags.Read);
        var samples = MemoryMarshal.Cast<byte, short>(map.Span);
        SamplesReady?.Invoke(samples);
    }

    public void OnMessage(Message message)
    {
        lock (_lock)
        {
            if (_disposed || _gate == null)
                return;
            var source = GstNative.GetMessageSource(message);
            if (source == _levelHandle || source == _voiceHandle)
                _gate.Update(source, message);
        }
    }

    public VoiceGateSummary? TakeGateSummary() { lock (_lock) return _disposed ? null : _gate?.TakeSummary(); }
    public void SetVolume(double volume) { lock (_lock) { if (!_disposed) _volume?.SetProperty("volume", volume); } }
    public void SetVoiceGate(VoiceGateOptions? options) { lock (_lock) { if (!_disposed) _gate?.SetOptions(options); } }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _reader.Dispose();
        _branch.SetState(State.Null);
        _pipeline.Remove(_branch);
        foreach (var wrapper in _wrappers)
            wrapper.Dispose();
        _branch.Dispose();
    }
}
