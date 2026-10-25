using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Client.Application.Ports;
using Client.Core.Settings;
using Gst;
using Gst.App;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Audio;

/// <summary>
/// Runs the selected input device through the same conversion, processing and gain the voice
/// pipeline will use, measures the peak level and can play the sound back to the default output.
/// </summary>
/// <remarks>
/// Samples, buffers and messages each own a reference and are disposed as soon as they are read.
/// </remarks>
public sealed partial class GstMicrophoneTester : IMicrophoneTester
{
    
    private const int MeterRate = 48000;
    private const int LevelReportSamples = MeterRate / 20;
    // The voice gate threshold is set against this meter, so both share the scale
    private const double MeterFloorDb = AudioSettings.MinVoiceGateThresholdDb;
    
    // The leaky queues keep the meter and the loopback from stalling each other or the capture
    private static string DescribeProcessing(NoiseSuppressionLevel noiseSuppression) =>
        "queue ! audioconvert ! audioresample ! " + MicrophoneProcessing.Caps + " ! " +
        MicrophoneProcessing.Fragment(noiseSuppression) + "volume name=input_volume ! tee name=split " +
        "split. ! queue leaky=downstream max-size-time=200000000 ! audioconvert ! audioresample " +
        $"! audio/x-raw,format=S16LE,channels=1,rate={MeterRate} " +
        "! appsink name=meter sync=false max-buffers=4 drop=true " +
        "split. ! queue leaky=downstream max-size-time=200000000 ! volume name=loopback_volume " +
        "! audioconvert ! audioresample ! autoaudiosink sync=false";

    // Also how long stopping may wait for the worker to notice
    private static readonly ClockTime PullTimeout = ClockTime.FromNanoseconds(100_000_000);
    private static readonly TimeSpan IdlePause = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan FailurePause = TimeSpan.FromMilliseconds(100);

    private readonly GstAudioDeviceProvider _deviceProvider;
    private readonly ILogger<GstMicrophoneTester> _logger;
    private readonly Lock _lock = new();
    
    private Pipeline? _pipeline;
    private Element? _inputVolume;
    private Element? _loopbackVolume;
    private Worker? _worker;

    // Every wrapper the test made, the pipeline last. The binding keeps an element alive for as
    // long as its wrapper is, and frees a dropped one only when something drains its release
    // queue, so they are all disposed when the test stops rather than left to the GC.
    private readonly List<Gst.GObject.Object> _wrappers = [];

    // Touched only on the worker thread
    private int _peak;
    private int _samplesSinceReport;
    
    public event Action<double>? LevelChanged;
    public event Action<string>? Failed;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _pipeline != null;
        }
    }

    public GstMicrophoneTester(GstAudioDeviceProvider deviceProvider, ILogger<GstMicrophoneTester> logger)
    {
        _deviceProvider = deviceProvider;
        _logger = logger;
    }

    public MicrophoneTestError? Start(string? deviceId, double volume, bool isLoopbackEnabled,
        NoiseSuppressionLevel noiseSuppression)
    {
        lock (_lock)
        {
            StopPipeline();

            try
            {
                var source = Track(_deviceProvider.CreateInputElement(deviceId));

                if (source == null)
                    return MicrophoneTestError.DeviceNotConnected;

                var pipeline = Pipeline.New("microphone-test");

                // From here on StopPipeline cleans up, also after a failure below
                _pipeline = pipeline;
                pipeline.Add(source);

                // The test goes through what a call would, and a call without RNNoise runs a neural level as the moderate one
                var applied = MicrophoneProcessing.Resolve(noiseSuppression);

                if (applied != noiseSuppression)
                    LogNoiseSuppressionReplaced(noiseSuppression, applied);

                if (Track(Global.ParseBinFromDescription(DescribeProcessing(applied), true)) is not Bin processing)
                {
                    StopPipeline();
                    return MicrophoneTestError.Unexpected;
                }

                pipeline.Add(processing);

                if (!source.Link(processing))
                {
                    StopPipeline();
                    return MicrophoneTestError.DeviceLinkFailed;
                }

                if (Track(processing.GetByName("meter")) is not AppSink meter)
                {
                    StopPipeline();
                    return MicrophoneTestError.Unexpected;
                }

                _inputVolume = Track(processing.GetByName("input_volume"));
                _loopbackVolume = Track(processing.GetByName("loopback_volume"));
                _inputVolume?.SetProperty("volume", volume);
                _loopbackVolume?.SetProperty("mute", !isLoopbackEnabled);

                _peak = 0;
                _samplesSinceReport = 0;

                // No GLib main loop runs in the app, so the bus is polled, and the sink is pulled
                // from rather than watched: its new-sample signal would run managed code on a
                // streaming thread
                _worker = new Worker(this, pipeline, meter, Track(pipeline.GetBus()));

                if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                {
                    StopPipeline();
                    return MicrophoneTestError.DeviceOpenFailed;
                }

                LogStarted(deviceId ?? "system default");
                return null;
            }
            catch (Exception exception)
            {
                LogStartFailed(exception);
                StopPipeline();
                return MicrophoneTestError.Unexpected;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopPipeline();
        }
    }

    public void SetVolume(double volume)
    {
        lock (_lock)
        {
            _inputVolume?.SetProperty("volume", volume);
        }
    }

    public void SetLoopbackEnabled(bool isEnabled)
    {
        lock (_lock)
        {
            _loopbackVolume?.SetProperty("mute", !isEnabled);
        }
    }

    private void StopPipeline()
    {
        var pipeline = _pipeline;
        
        if (pipeline == null)
            return;

        _pipeline = null;
        
        // Stopped first: the worker must not pull from a sink that is shutting down
        _worker?.Dispose();
        _worker = null;
        pipeline.SetState(State.Null);
        
        _inputVolume = null;
        _loopbackVolume = null;

        foreach (var wrapper in _wrappers)
            wrapper.Dispose();

        _wrappers.Clear();
        pipeline.Dispose();
        
        LevelChanged?.Invoke(0);
        LogStopped();
    }

    [return: NotNullIfNotNull(nameof(wrapper))]
    private T? Track<T>(T? wrapper) where T : Gst.GObject.Object
    {
        if (wrapper != null)
            _wrappers.Add(wrapper);

        return wrapper;
    }

    private void OnMeterSample(Sample sample)
    {
        // The wrapper holds a native reference until disposed. Leaving the buffer to the GC drains
        // the fixed buffer pool of pipewiresrc, which then stops producing after a few buffers.
        using var buffer = sample.GetBuffer();

        if (buffer == null)
            return;

        using (var map = buffer.Map(MapFlags.Read))
        {
            var samples = MemoryMarshal.Cast<byte, short>(map.Span);

            foreach (var value in samples)
            {
                var magnitude = Math.Abs((int)value);
                
                if (magnitude > _peak)
                    _peak = magnitude;
            }

            _samplesSinceReport += samples.Length;
        }

        if (_samplesSinceReport < LevelReportSamples)
            return;
        
        LevelChanged?.Invoke(ToMeterLevel(_peak));
        _peak = 0;
        _samplesSinceReport = 0;
    }

    /// <summary>
    /// Maps the peak onto a decibel scale, which is how loudness is perceived: on a linear scale
    /// normal speech barely moves the meter.
    /// </summary>
    private static double ToMeterLevel(int peak)
    {
        if (peak == 0)
            return 0;
        
        var decibels = 20 * Math.Log10(peak / 32768.0);
        return Math.Clamp((decibels - MeterFloorDb) / -MeterFloorDb, 0, 1);
    }

    private void OnBusError(Pipeline pipeline, Message message)
    {
        var (error, debug) = message.ParseError();
        LogFailed(error.Message, debug);
        
        // Stopping waits for the worker thread this runs on
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_lock)
            {
                if (_pipeline != pipeline)
                    return;
                
                StopPipeline();
            }
            
            Failed?.Invoke(error.Message);
        });
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// The thread of a running test: takes the samples out of the meter sink and the errors off
    /// the bus.
    /// </summary>
    private sealed class Worker : IDisposable
    {
        private readonly GstMicrophoneTester _owner;
        private readonly Pipeline _pipeline;
        private readonly AppSink _meter;
        private readonly Bus _bus;
        private readonly Thread _thread;
        private volatile bool _isStopped;

        public Worker(GstMicrophoneTester owner, Pipeline pipeline, AppSink meter, Bus bus)
        {
            _owner = owner;
            _pipeline = pipeline;
            _meter = meter;
            _bus = bus;
            _thread = new Thread(Run) { IsBackground = true, Name = "MicrophoneTest" };
            _thread.Start();
        }

        private void Run()
        {
            while (!_isStopped)
            {
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var hasSample = false;

                    // Disposed here rather than left to the GC: a sample holds a buffer of the pool it
                    // came from, and a source with a fixed pool stops producing once they are all held
                    using (var sample = _meter.TryPullSample(PullTimeout))
                    {
                        if (sample != null)
                        {
                            hasSample = true;
                            _owner.OnMeterSample(sample);
                        }
                    }

                    using (var message = _bus.PopFiltered(MessageType.Error))
                    {
                        if (message != null && !_isStopped)
                            _owner.OnBusError(_pipeline, message);
                    }

                    // The binding queues the releases of collected wrappers for a main loop, and the
                    // app runs none
                    Gst.GObject.Object.DrainPendingReleases();

                    // A sink that is not running yet, or not any more, answers at once instead of waiting
                    if (!hasSample && Stopwatch.GetElapsedTime(started) < IdlePause)
                        Thread.Sleep(IdlePause);
                }
                catch (Exception exception)
                {
                    if (_isStopped)
                        return;

                    _owner.LogMeterFailed(exception);

                    // A sink that keeps failing must not spin the thread
                    Thread.Sleep(FailurePause);
                }
            }
        }

        /// <summary>
        /// Stops the thread and waits for a sample that is being handled, unless called from the
        /// thread itself. Call before the pipeline is shut down.
        /// </summary>
        public void Dispose()
        {
            if (_isStopped)
                return;

            _isStopped = true;

            if (Thread.CurrentThread != _thread)
                _thread.Join();
        }
    }
}
