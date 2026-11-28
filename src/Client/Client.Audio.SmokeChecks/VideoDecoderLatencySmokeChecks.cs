using System.Diagnostics;
using Client.Core.Media;
using Client.Core.Settings;
using Client.GStreamer.Interop;
using Client.GStreamer.Video;
using Gst;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Every H.264 decoder of this system through the decoder of a real stream: a picture must come
/// out for each frame pushed, without waiting for the next one, as a decoder that reorders by the size
/// of its picture buffer would. Then 1080p at 60 frames per second, paced, for the decode time.
/// Frames come from the software encoders, so a decoder is tested apart from the encoder of its GPU.
/// </summary>
internal static class VideoDecoderLatencySmokeChecks
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int Fps = 60;

    public static void Run()
    {
        var previous = VideoSettings.Current;
        var failures = new List<string>();

        try
        {
            foreach (var (codec, encoder) in new[] { (VideoCodec.H264, "openh264enc") })
            {
                foreach (var decoder in VideoDecoderSelector.GetAvailableDecoders(codec))
                {
                    VideoSettings.Apply(previous with
                    {
                        IsHardwareDecodingEnabled = true,
                        Decoder = codec == VideoCodec.H264 ? decoder : previous.Decoder,
                        Av1Decoder = codec == VideoCodec.AV1 ? decoder : previous.Av1Decoder
                    });
                    try
                    {
                        Check(codec, encoder, decoder);
                    }
                    catch (InvalidOperationException exception)
                    {
                        Console.WriteLine("FAIL " + exception.Message);
                        failures.Add(exception.Message);
                    }
                }
            }
        }
        finally
        {
            VideoSettings.Apply(previous);
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join("; ", failures));

        Console.WriteLine("Video decoder latency smoke checks passed.");
    }

    private static void Check(VideoCodec codec, string encoderFactory, string decoderFactory)
    {
        var name = $"{codec} {decoderFactory}";
        var choice = VideoEncoderSelector.Select(codec, 8000, encoderFactory)
            ?? throw new InvalidOperationException($"{name}: no encoder");

        if (choice.Factory != encoderFactory)
            throw new InvalidOperationException($"{name}: {encoderFactory} is missing");

        using var decoder = GstVideoDecoder.Create(codec, "latency_smoke", NullLogger.Instance)
            ?? throw new InvalidOperationException($"{name}: cannot create the decoder");
        using var pipeline = (Pipeline)Global.ParseLaunch(
            "appsrc name=raw is-live=true do-timestamp=true format=time " +
            $"caps=\"video/x-raw,format=I420,width={Width},height={Height},framerate={Fps}/1\" ! " +
            choice.ToLaunchString("encoder") + " ! " + VideoCodecPipeline.For(codec).Capture +
            " ! appsink name=encoded sync=false async=false enable-last-sample=false wait-on-eos=false");
        using var source = pipeline.GetByName("raw")!;
        using var arrived = new AutoResetEvent(false);
        var decoded = 0;

        decoder.FrameReady += _ =>
        {
            Interlocked.Increment(ref decoded);
            arrived.Set();
        };

        using var reader = new AppSinkReader(pipeline.GetByName("encoded")!, "latency_smoke",
            sample =>
            {
                using var buffer = sample.GetBuffer()!;
                GstEncodedNative.Read(buffer, decoder.Push);
            }, error => Console.WriteLine(error));

        var pixels = new byte[Width * Height * 3 / 2];
        pixels.AsSpan(0, Width * Height).Fill(200);
        pixels.AsSpan(Width * Height).Fill(128);

        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException($"{name}: cannot start the encoder");

            for (var index = 0; index < 8; index++)
            {
                Push(index);

                if (!arrived.WaitOne(TimeSpan.FromSeconds(2)))
                    throw new InvalidOperationException(
                        $"{name}: frame {index} is held until more input comes ({Volatile.Read(ref decoded)} decoded)");
            }

            decoder.TakePerf();
            var started = Stopwatch.GetTimestamp();
            var before = Volatile.Read(ref decoded);

            for (var index = 0; index < Fps * 2; index++)
            {
                var due = (index + 1.0) / Fps;

                while (Stopwatch.GetElapsedTime(started).TotalSeconds < due)
                    Thread.Sleep(1);

                Push(index + 8);
            }

            Thread.Sleep(200);
            var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var count = Volatile.Read(ref decoded) - before;
            var perf = decoder.TakePerf();

            Console.WriteLine($"PASS {name}: no frame held; 1080p/{Fps}: {count / seconds:F1} decoded fps, " +
                $"decode p95 {perf?.DecodeMs.P95Ms:F1} ms, max {perf?.DecodeMs.MaxMs:F1} ms, dropped {perf?.Dropped}");

            if (count < Fps * 2 * 9 / 10)
                throw new InvalidOperationException($"{name}: only {count} of {Fps * 2} frames decoded in time");
        }
        finally
        {
            reader.Dispose();
            pipeline.SetState(State.Null);
        }

        void Push(int index)
        {
            // A moving edge, so every frame differs from the one before
            var row = index * 8 % Height;
            pixels.AsSpan(row * Width, Width).Fill((byte)(index % 2 == 0 ? 32 : 220));
            using var buffer = Gst.Buffer.NewMemdup(pixels);
            GstApp.PushBuffer(source, buffer);
        }
    }
}
