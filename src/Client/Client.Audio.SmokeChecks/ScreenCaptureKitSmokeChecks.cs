using System.Diagnostics;
using System.Runtime.Versioning;
using Client.Application.Ports;
using Client.GStreamer.Audio;
using Client.GStreamer.Interop;
using Client.GStreamer.Video;
using Client.GStreamer.Video.Mac;
using Gst;

/// <summary>
/// The capture of macOS through libflowrate_sck: the lists of displays and windows, a few seconds of
/// the main display and of the sound of everything but this process. Needs Screen Recording for the
/// application that runs it (the terminal); without it only the library and the lists are checked.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ScreenCaptureKitSmokeChecks
{
    public static void Run()
    {
        if (!ScreenCaptureKit.IsAvailable)
            throw new InvalidOperationException("libflowrate_sck.dylib is missing or does not match the bindings");

        var displays = ScreenCaptureKit.GetDisplays();
        var windows = ScreenCaptureKit.GetWindows();
        Console.WriteLine($"Screen Recording allowed: {ScreenCaptureKit.HasAccess}");
        Console.WriteLine($"Displays: {string.Join(", ", displays.Select(d => $"{d.Id} '{d.Title}' {d.Width}x{d.Height}"))}");
        Console.WriteLine($"Windows: {windows.Count}");

        foreach (var window in windows.Take(5))
            Console.WriteLine($"  {window.Id} pid={window.ProcessId} '{window.Title}' {window.Width}x{window.Height}");

        if (!ScreenCaptureKit.HasAccess)
        {
            Console.WriteLine("Screen Recording is not allowed for this terminal: capture checks skipped.");
            return;
        }

        if (displays.Count == 0)
            throw new InvalidOperationException("No display listed although Screen Recording is allowed");

        CheckVideo(new MonitorVideoInput(new IntPtr((long)displays[0].Id)), "display");

        if (windows.Count > 0)
            CheckVideo(new WindowVideoInput(new IntPtr((long)windows[0].Id)), $"window '{windows[0].Title}'");

        CheckEncoding(new MonitorVideoInput(new IntPtr((long)displays[0].Id)));
        CheckAudio();
        Console.WriteLine("ScreenCaptureKit smoke checks passed.");
    }

    private static void CheckVideo(VideoInput input, string name)
    {
        var errors = new List<Exception>();
        var source = VideoSourceFactory.Create(input, 30, errors.Add, maxHeight: 720)
            ?? throw new InvalidOperationException($"Cannot create the capture of the {name}");

        using var pipeline = Pipeline.New("sck_video");
        using var branch = Global.ParseBinFromDescription(
            "videoconvert ! video/x-raw,format=I420 ! appsink name=frames sync=false max-buffers=4 drop=true", true);
        using var sink = branch.GetByName("frames")!;
        pipeline.Add(source.Element);
        pipeline.Add(branch);

        try
        {
            if (!source.Element.Link(branch) || pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException($"Cannot play the capture of the {name}");

            // A still screen too: the keepalive repeats the last picture every 100 ms
            var frames = 0;
            string? caps = null;
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed < TimeSpan.FromSeconds(3))
            {
                using var sample = GstApp.TryPullSample(sink, 500_000_000);

                if (sample == null)
                    continue;

                frames++;
                using var sampleCaps = sample.GetCaps();
                caps ??= sampleCaps?.ToString();
            }

            Console.WriteLine($"Capture of the {name}: {frames} frames in 3 s, {caps}");

            if (frames < 10)
                throw new InvalidOperationException($"Too few frames from the {name}: {frames}");

            if (errors.Count > 0)
                throw new InvalidOperationException($"The capture of the {name} failed", errors[0]);
        }
        finally
        {
            source.Resource?.Dispose();
            pipeline.SetState(State.Null);
            source.Element.Dispose();
        }
    }

    // The capture through the branch of a real share: the encoder of macOS chosen first must take what
    // the capture hands over and give a baseline the SFU takes
    private static void CheckEncoding(VideoInput input)
    {
        var errors = new List<Exception>();
        var settings = new Client.Core.Media.ScreenShareSettings();
        var source = VideoSourceFactory.Create(input, settings.Framerate, errors.Add, maxHeight: settings.MaxHeight)
            ?? throw new InvalidOperationException("Cannot create the capture to encode");
        var branch = ScreenEncodingBranch.Build(Client.Core.Media.VideoCodec.H264, settings, onError: errors.Add)
            ?? throw new InvalidOperationException("Cannot build the H.264 branch", errors.FirstOrDefault());

        using var pipeline = Pipeline.New("sck_encode");
        pipeline.Add(source.Element);
        pipeline.Add(branch.Bin);

        try
        {
            if (!source.Element.Link(branch.Bin) || pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException("Cannot play the encoding of the capture");

            var frames = 0;
            string? caps = null;
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed < TimeSpan.FromSeconds(3))
            {
                using var sample = GstApp.TryPullSample(branch.Encoded, 500_000_000);

                if (sample == null)
                    continue;

                frames++;
                using var sampleCaps = sample.GetCaps();
                caps ??= sampleCaps?.ToString();
            }

            Console.WriteLine($"Encoded by {branch.Encoder.Factory} (hardware: {branch.Encoder.IsHardware}): " +
                $"{frames} frames in 3 s, {caps}");

            if (frames < 10 || caps?.Contains("baseline") != true)
                throw new InvalidOperationException($"The encoding of the capture failed: {frames} frames, {caps}");
        }
        finally
        {
            source.Resource?.Dispose();
            pipeline.SetState(State.Null);
        }
    }

    private static void CheckAudio()
    {
        var errors = new List<Exception>();
        using var source = ScreenCaptureKitAudioSource.Create(
                new ProcessLoopbackAudioInput((uint)Environment.ProcessId, IsExcluded: true), errors.Add)
            ?? throw new InvalidOperationException("Cannot create the sound capture", errors.FirstOrDefault());

        using var pipeline = Pipeline.New("sck_audio");
        using var branch = Global.ParseBinFromDescription(
            "audioconvert ! audioresample ! audio/x-raw,format=S16LE,rate=48000,channels=1 ! " +
            "appsink name=samples sync=false max-buffers=50 drop=true", true);
        using var sink = branch.GetByName("samples")!;
        pipeline.Add(source.Element);
        pipeline.Add(branch);

        try
        {
            if (!source.Element.Link(branch) || pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException("Cannot play the sound capture");

            var buffers = 0;
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed < TimeSpan.FromSeconds(3))
            {
                using var sample = GstApp.TryPullSample(sink, 500_000_000);

                if (sample != null)
                    buffers++;
            }

            // The system hands over nothing while nothing plays, so silence is not a failure
            Console.WriteLine($"Sound capture: {buffers} buffers in 3 s");

            if (errors.Count > 0)
                throw new InvalidOperationException("The sound capture failed", errors[0]);
        }
        finally
        {
            source.Dispose();
            pipeline.SetState(State.Null);
        }
    }
}
