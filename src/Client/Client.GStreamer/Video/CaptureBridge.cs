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

    /// <param name="sink">The appsink the capture chain ends in.</param>
    /// <param name="source">The appsrc the rest of the branch starts from. The bridge takes over the wrapper.</param>
    /// <param name="onError">Receives a failure to hand a frame over, on the thread of the bridge.</param>
    /// <param name="onRate">
    /// Receives every few seconds how many frames per second the capture delivers and their caps.
    /// </param>
    public CaptureBridge(Element sink, Element source, Action<Exception> onError, Action<double, string>? onRate = null)
    {
        _source = source;
        _onRate = onRate;

        // A push is refused while the branch is not running; the next frame is tried again
        _reader = new AppSinkReader(sink, "capture_bridge", Forward, onError);
    }

    private void Forward(Sample sample)
    {
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
        _source.Dispose();
    }
}
