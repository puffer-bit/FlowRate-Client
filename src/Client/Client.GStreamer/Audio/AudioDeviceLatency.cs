using Gst;

namespace Client.GStreamer.Audio;

/// <summary>Bounds hardware buffering without changing the device or its clock.</summary>
internal static class AudioDeviceLatency
{
    public const long BufferTimeMicroseconds = 60_000;
    public const long PeriodTimeMicroseconds = 10_000;

    public static Element? ConfigureCapture(Element? source)
    {
        if (source == null)
            return null;

        Configure(source);
        // autoaudiosrc creates the hardware source when entering READY. Configure it before
        // it opens the device, too; properties on the enclosing bin do not reach that source.
        if (source is Bin bin)
        {
            source.ConnectSignal("deep-element-added", (_, args) =>
            {
                if (args is [Bin, Element child])
                    Configure(child);
                return null;
            });
            using var children = bin.IterateRecurse();
            foreach (var child in children.Items<Element>())
            {
                using (child)
                    Configure(child);
            }
        }
        return source;
    }

    private static void Configure(Element element)
    {
        if (element.FindProperty("buffer-time") != null)
            element.SetProperty("buffer-time", BufferTimeMicroseconds);
        if (element.FindProperty("latency-time") != null)
            element.SetProperty("latency-time", PeriodTimeMicroseconds);
        if (element.FindProperty("low-latency") != null)
            // WASAPI's low-latency mode selects the hardware minimum period instead of
            // the requested 10 ms. Managed capture cannot reliably meet sub-10 ms deadlines.
            element.SetProperty("low-latency", false);
    }
}
