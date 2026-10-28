using System.Globalization;
using Client.Application.Ports;
using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Audio;

/// <summary>
/// The capture of one sound in a pipeline of its own: the source of the input, converted to the
/// samples of <see cref="IAudioCapture"/> and read out of a sink on a thread of its own.
/// </summary>
internal sealed unsafe partial class GstAudioCapture : IAudioCapture
{
    private const string SinkName = "samples";

    // A late piece of the sound is worth nothing, so the sink drops rather than holds the source back
    private static readonly string Description = string.Format(CultureInfo.InvariantCulture,
        "audioconvert ! audioresample ! audio/x-raw,format=S16LE,rate={0},channels={1},layout=interleaved ! " +
        "appsink name=" + SinkName + " max-buffers=20 drop=true sync=false async=false",
        IAudioCapture.SampleRate, IAudioCapture.Channels);

    private readonly string _name;
    private readonly ILogger _logger;
    private readonly Pipeline _pipeline;
    private readonly Element _sink;
    private readonly IDisposable? _resource;
    private readonly Lock _lock = new();

    private GstBusWatcher? _bus;
    private AppSinkReader? _reader;
    private bool _isStarted;
    private bool _isDisposed;

    public event AudioSamplesHandler? SamplesReady;

    private GstAudioCapture(string name, Pipeline pipeline, Element sink, IDisposable? resource, ILogger logger)
    {
        _name = name;
        _pipeline = pipeline;
        _sink = sink;
        _resource = resource;
        _logger = logger;
    }

    /// <returns>Null when the input cannot be captured here or an element is missing.</returns>
    public static GstAudioCapture? Create(AudioInput input, string name, ILogger logger)
    {
        var (source, resource) = CreateSource(input, logger, name);

        if (source == null)
        {
            LogSourceCreateFailed(logger, name, input);
            return null;
        }

        var branch = Global.ParseBinFromDescription(Description, true);

        if (branch?.GetByName(SinkName) is not { } sink)
        {
            branch?.Dispose();
            resource?.Dispose();
            source.Dispose();
            LogBranchBuildFailed(logger, name);
            return null;
        }

        var pipeline = Pipeline.New($"sound_{name}");
        pipeline.Add(source);
        pipeline.Add(branch);

        if (!source.Link(branch))
        {
            LogBranchBuildFailed(logger, name);
            resource?.Dispose();
            sink.Dispose();
            pipeline.SetState(State.Null);
            pipeline.Dispose();
            return null;
        }

        // The pipeline holds them from here on
        source.Dispose();
        branch.Dispose();

        return new GstAudioCapture(name, pipeline, sink, resource, logger);
    }

    private static (Element? Source, IDisposable? Resource) CreateSource(AudioInput input, ILogger logger, string name)
    {
        switch (input)
        {
            case ProcessLoopbackAudioInput loopback:
                return (ProcessLoopbackSource.Create(loopback), null);
            case PipeWireAppAudioInput app:
                var pipeWire = PipeWireAppAudioSource.Create(app, exception => LogAppStreamsFailed(logger, exception, name));
                return (pipeWire?.Element, pipeWire);
            case FileAudioInput or ToneAudioInput:
                return (FileAudioSource.CreateElement(input), null);
            default:
                return (null, null);
        }
    }

    public bool Start()
    {
        lock (_lock)
        {
            if (_isDisposed || _isStarted)
                return false;

            _isStarted = true;
            _bus = new GstBusWatcher(_pipeline.GetBus(), MessageType.Error | MessageType.Warning, OnMessage,
                $"sound_{_name}", _logger);

            _reader = new AppSinkReader(_sink, $"sound_{_name}", sample =>
                {
                    using var buffer = sample.GetBuffer();

                    if (buffer != null && SamplesReady is { } onSamples)
                        GstEncodedNative.Read(buffer, data =>
                            onSamples(new ReadOnlySpan<short>((void*)data.Data, data.Length / sizeof(short))));
                },
                exception => LogSamplesFailed(_logger, exception, _name));

            if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                LogStartFailed(_logger, _name);
                return false;
            }
        }

        LogStarted(_logger, _name);
        return true;
    }

    private void OnMessage(Message message)
    {
        var (error, debug) = message.Type == MessageType.Error ? message.ParseError() : message.ParseWarning();
        LogPipelineMessage(_logger, _name, message.Type, GstNative.GetPath(GstNative.GetMessageSource(message)),
            error.Message, debug);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
        }

        // The reader and the source work on elements of the pipeline, so they stop first
        _reader?.Dispose();
        _resource?.Dispose();

        _pipeline.SetState(State.Null);
        _bus?.Dispose();

        if (_reader == null)
            _sink.Dispose();

        _pipeline.Dispose();
    }

    [LoggerMessage(LogLevel.Error, "[Sound {Name}] Failed to create the audio source for {Input}")]
    private static partial void LogSourceCreateFailed(ILogger logger, string name, AudioInput input);

    [LoggerMessage(LogLevel.Error, "[Sound {Name}] Failed to build the conversion: an element is missing or cannot be linked")]
    private static partial void LogBranchBuildFailed(ILogger logger, string name);

    [LoggerMessage(LogLevel.Error, "[Sound {Name}] Failed to start the pipeline")]
    private static partial void LogStartFailed(ILogger logger, string name);

    [LoggerMessage(LogLevel.Information, "[Sound {Name}] Capturing")]
    private static partial void LogStarted(ILogger logger, string name);

    [LoggerMessage(LogLevel.Warning, "[Sound {Name}] Failed to hand over captured samples")]
    private static partial void LogSamplesFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(LogLevel.Warning, "[Sound {Name}] Failed to follow the streams of the application")]
    private static partial void LogAppStreamsFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(LogLevel.Warning, "[Sound {Name}] {Type} from {Source}: {Message} ({Debug})")]
    private static partial void LogPipelineMessage(ILogger logger, string name, MessageType type, string source,
        string message, string? debug);
}
