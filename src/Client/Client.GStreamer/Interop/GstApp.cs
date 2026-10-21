using Gst;
using Gst.App;

namespace Client.GStreamer.Interop;

/// <summary>
/// The calls of appsink and appsrc, made on an element parsed from a description.
/// </summary>
/// <remarks>
/// Such an element comes back as a plain <see cref="Element"/> reference; the wrapper behind it is
/// the typed one as long as the app library was initialized, see GstLoader.
/// </remarks>
internal static class GstApp
{
    /// <summary>
    /// Waits for the next sample of an appsink.
    /// </summary>
    /// <returns>Null when nothing arrived in time, or the sink is not running or has ended.</returns>
    public static Sample? TryPullSample(Element appsink, ulong timeoutNanoseconds) =>
        ((AppSink)appsink).TryPullSample(ClockTime.FromNanoseconds(timeoutNanoseconds));

    /// <summary>
    /// Hands a buffer to an appsrc, which takes it over.
    /// </summary>
    public static FlowReturn PushBuffer(Element appsrc, Gst.Buffer buffer) =>
        ((AppSrc)appsrc).PushBuffer(buffer);

    /// <summary>
    /// Hands the buffer of a sample to an appsrc, which also takes over the caps of the sample when
    /// they differ from its own. The caller keeps its own reference to the sample.
    /// </summary>
    public static FlowReturn PushSample(Element appsrc, Sample sample) =>
        ((AppSrc)appsrc).PushSample(sample);
}
