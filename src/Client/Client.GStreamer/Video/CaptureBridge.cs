using System.Diagnostics;
using Gst;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Video;

/// <summary>
/// Carries the frames of a capture chain that ends in an appsink over to an appsrc, from which the
/// rest of the branch continues. The buffers are handed over as they are, nothing is copied.
/// </summary>
/// <remarks>
/// The two halves negotiate their formats separately, which is the point. pipewiresrc turns
/// whatever the elements after it would accept into the format it asks the compositor for, and
/// the size limits of a scaler or an encoder make that request fail: the capture never starts,
/// with not-negotiated. Ending the chain in a sink that takes any size keeps the request to what
/// is known to work, and the appsrc side is free to scale and encode.
/// </remarks>
internal sealed class CaptureBridge : IDisposable
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);

    private readonly AppSinkReader _reader;
    private readonly Element _source;
    private readonly Action<double, string>? _onRate;
    private readonly System.Diagnostics.Stopwatch _rateClock = System.Diagnostics.Stopwatch.StartNew();
    private int _frames;
    private readonly double _frameInterval;
    private double _nextFrame;
    private readonly int? _encoderFramerate;
    private Caps? _captureCaps;
    private Caps? _encoderCaps;

    /// <param name="sink">The appsink the capture chain ends in.</param>
    /// <param name="source">The appsrc the rest of the branch starts from. The bridge takes over the wrapper.</param>
    /// <param name="onError">Receives a failure to hand a frame over, on the thread of the bridge.</param>
    /// <param name="onRate">
    /// Receives every few seconds how many frames per second the capture delivers and their caps.
    /// </param>
    /// <param name="maxFramerate">Ceiling for forwarding frames; skipped samples are released immediately.</param>
    /// <param name="setEncoderFramerate">
    /// Advertises that ceiling downstream for encoder rate control, while preserving capture timestamps.
    /// </param>
    public CaptureBridge(Element sink, Element source, Action<Exception> onError, Action<double, string>? onRate = null,
        int? maxFramerate = null, bool setEncoderFramerate = false)
    {
        _source = source;
        _onRate = onRate;
        if (maxFramerate is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFramerate));
        _frameInterval = maxFramerate is { } rate ? (double)Stopwatch.Frequency / rate : 0;
        _encoderFramerate = setEncoderFramerate ? maxFramerate : null;

        // A push is refused while the branch is not running; the next frame is tried again
        _reader = new AppSinkReader(sink, "capture_bridge", Forward, onError);
    }

    private void Forward(Sample sample)
    {
        // VFR capture negotiates framerate=0/1. Enforce the configured ceiling here,
        // independent of videorate negotiation, without sleeping or retaining a sample.
        // Carry the fractional remainder so 165 Hz -> 144 Hz does not become 82.5 Hz.
        if (_frameInterval > 0)
        {
            var now = Stopwatch.GetTimestamp();
            if (now < _nextFrame)
                return;
            _nextFrame = _nextFrame == 0
                ? now + _frameInterval
                : Math.Max(_nextFrame + _frameInterval, now);
        }

        if (_encoderFramerate is { } framerate)
        {
            // PipeWire's VFR caps say 0/1. VA H.264 substitutes 30 fps for that,
            // including in the driver's rate control, even when we forward 144 fps.
            // Advertise the ceiling already enforced above on the encoder side only.
            // Keep the original timestamps, GPU buffer, caps features and segment.
            using var inputCaps = sample.GetCaps();
            if (inputCaps != null)
            {
                if (_captureCaps == null || !_captureCaps.IsEqual(inputCaps))
                {
                    _captureCaps?.Dispose();
                    _encoderCaps?.Dispose();
                    _captureCaps = inputCaps.Copy();
                    _encoderCaps = inputCaps.Copy()!;
                    using var rateCaps = Caps.FromString(FormattableString.Invariant($"video/x-raw,framerate={framerate}/1"))!;
                    using var rateStructure = rateCaps.GetStructure(0);
                    using var value = rateStructure.GetValue("framerate");
                    _encoderCaps.SetValue("framerate", in value);
                }

                using var forwarded = sample.Copy()!;
                forwarded.SetCaps(_encoderCaps!);
                GstApp.PushSample(_source, forwarded);
            }
            else
                GstApp.PushSample(_source, sample);
        }
        else
            GstApp.PushSample(_source, sample);

        _frames++;

        if (_onRate == null || _rateClock.Elapsed < ReportInterval)
            return;

        using var caps = sample.GetCaps();
        _onRate(_frames / _rateClock.Elapsed.TotalSeconds, caps?.ToString() ?? "");
        _frames = 0;
        _rateClock.Restart();
    }

    /// <summary>
    /// Stops the thread. Call before the elements are shut down.
    /// </summary>
    public void Dispose()
    {
        _reader.Dispose();
        _captureCaps?.Dispose();
        _encoderCaps?.Dispose();
        _source.Dispose();
    }
}
