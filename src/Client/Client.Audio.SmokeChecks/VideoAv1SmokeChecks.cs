using System.Diagnostics;
using Client.Core.Media;
using Client.Core.Settings;
using Client.GStreamer.Interop;
using Client.GStreamer.Video;
using Gst;
using Microsoft.Extensions.Logging.Abstractions;

internal static class VideoAv1SmokeChecks
{
    public static void Run()
    {
        if (!OperatingSystem.IsLinux() || ElementFactory.Find("vaav1enc") == null ||
            ElementFactory.Find("vaav1dec") == null)
            throw new InvalidOperationException("This check requires Linux VA-API AV1 encoding and decoding");

        const int width = 1920;
        const int height = 1080;
        const int fps = 144;
        var previousSettings = VideoSettings.Current;
        var previousSurfaces = VideoSurfaceSupport.Kinds;
        try
        {
            VideoSettings.Apply(previousSettings with
            {
                IsHardwareDecodingEnabled = true, Av1Decoder = "vaav1dec", IsZeroCopyEnabled = true
            });
            // Exercise the production DMA-BUF decoder without retaining surfaces in a presenter.
            VideoSurfaceSupport.Set(VideoSurfaceKinds.DmaBuf);
            var choice = VideoEncoderSelector.Select(VideoCodec.AV1, 25000, "vaav1enc")!;
            using (var probe = ElementFactory.Make("vaav1enc", null)!)
            {
                if (GstVaContext.UsesMesaGallium(probe) &&
                    !choice.Properties.Contains(("target-usage", "1")))
                    throw new InvalidOperationException("Mesa AV1 must use its recommended quality preset");
            }
            Console.WriteLine("AV1 encoder: " + choice.ToLaunchString());
            using var decoder = GstVideoDecoder.Create(VideoCodec.AV1, "av1_smoke", NullLogger.Instance)
                ?? throw new InvalidOperationException("Cannot create VA-API AV1 decoder");
            using var pipeline = (Pipeline)Global.ParseLaunch(
                "appsrc name=raw is-live=true format=time " +
                $"caps=\"video/x-raw,format=NV12,width={width},height={height},framerate={fps}/1\" ! " +
                choice.ToLaunchString("encoder") + " ! " + VideoCodecPipeline.For(VideoCodec.AV1).Capture +
                " ! appsink name=encoded sync=false async=false enable-last-sample=false wait-on-eos=false");
            using var encoder = pipeline.GetByName("encoder")!;
            using var timer = GstCodecTimer.Attach(encoder);
            using var source = pipeline.GetByName("raw")!;
            using var arrived = new AutoResetEvent(false);
            var decoded = 0;
            decoder.FrameReady += frame =>
            {
                Interlocked.Increment(ref decoded);
                arrived.Set();
            };
            using var reader = new AppSinkReader(pipeline.GetByName("encoded")!, "av1_smoke",
                sample =>
                {
                    using var buffer = sample.GetBuffer()!;
                    GstEncodedNative.Read(buffer, decoder.Push);
                }, error => Console.WriteLine(error));
            var pixels = new byte[width * height * 3 / 2];
            pixels.AsSpan(0, width * height).Fill(200);
            pixels.AsSpan(width * height).Fill(128);
            try
            {
                if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                    throw new InvalidOperationException("Cannot start VA-API AV1 pipeline");

                // Sending the next frame must not be required to release the current picture.
                // A reordering encoder emits the initial keyframe, then waits for its future refs.
                for (var index = 0; index < 4; index++)
                {
                    Push(index);
                    if (!arrived.WaitOne(TimeSpan.FromSeconds(2)))
                        throw new InvalidOperationException($"AV1 retained frame {index} waiting for future input");
                }
                Console.WriteLine("PASS: AV1 keyframe and delta frames decode without future input");

                timer?.Take();
                decoder.TakePerf();
                var started = Stopwatch.GetTimestamp();
                var before = Volatile.Read(ref decoded);
                for (var index = 0; index < fps * 2; index++)
                {
                    var due = (index + 1.0) / fps;
                    while (Stopwatch.GetElapsedTime(started).TotalSeconds < due)
                        Thread.Sleep(1);
                    Push(index + 4);
                }
                var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
                var count = Volatile.Read(ref decoded) - before;
                var perf = decoder.TakePerf();
                var encoding = timer?.Take();
                Console.WriteLine($"AV1 1080p/{fps}: {count / seconds:F1} decoded fps; " +
                    $"queue max {perf?.MaxQueued}, dropped {perf?.Dropped}, decode p95 {perf?.DecodeMs.P95Ms:F1} ms");
                Console.WriteLine($"Encoder: {encoding?.Out / seconds:F1} fps, p95 {encoding?.Durations.P95Ms:F1} ms");
                if (count == 0)
                    throw new InvalidOperationException("AV1 did not deliver paced frames");
            }
            finally
            {
                reader.Dispose();
                pipeline.SetState(State.Null);
            }

            void Push(int index)
            {
                // Move a sharp monochrome edge while keeping CPU frame preparation inexpensive.
                var row = index % height;
                pixels.AsSpan(row * width, width).Fill((byte)(index % 2 == 0 ? 32 : 220));
                using var buffer = Gst.Buffer.NewMemdup(pixels);
                GstEncodedNative.Read(buffer, frame =>
                    GstEncodedNative.Push(source, frame with { Timestamp = (long)index * 1_000_000_000 / fps }));
            }
        }
        finally
        {
            VideoSettings.Apply(previousSettings);
            VideoSurfaceSupport.Set(previousSurfaces);
        }
    }
}
