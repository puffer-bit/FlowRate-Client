using Client.GStreamer.Video;
using Client.GStreamer.Interop;
using Gst;

internal static class CameraSmokeChecks
{
    public static void Run()
    {
        var devices = new GstCameraDeviceProvider().GetDevices();
        Console.WriteLine($"Camera discovery completed: {devices.Count} device(s).");
        var preferred = VideoSourceFactory.SelectCameraCaps(
            "video/x-raw,width=1920,height=1080,framerate=5/1; image/jpeg,width=1280,height=720,framerate=30/1", 720, 30);
        if (preferred?.Contains("image/jpeg") != true || !preferred.Contains("30/1"))
            throw new InvalidOperationException("Camera native mode selection ignored the requested FPS");
        foreach (var jpeg in new[] { false, true })
        {
            // Synthetic frames exercise both common USB camera formats without opening a device.
            using var input = Global.ParseBinFromDescription(
                "appsrc name=raw is-live=true format=time do-timestamp=true " +
                "caps=\"video/x-raw,format=I420,width=320,height=240,framerate=30/1\"" +
                (jpeg ? " ! jpegenc" : ""), true);
            using var raw = input.GetByName("raw")!;
            var camera = VideoSourceFactory.CreateCamera(input)
                ?? throw new InvalidOperationException("Cannot create camera decode bridge");
            using var pipeline = Pipeline.New("camera_smoke");
            using var sink = ElementFactory.Make("appsink", "frames")!;
            sink.SetProperty("sync", false);
            pipeline.Add(camera.Element);
            pipeline.Add(sink);
            try
            {
                if (!camera.Element.Link(sink) || pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                    throw new InvalidOperationException("Cannot start camera bridge");
                using var buffer = Gst.Buffer.NewMemdup(new byte[320 * 240 * 3 / 2]);
                GstApp.PushBuffer(raw, buffer);
                using var sample = GstApp.TryPullSample(sink, 5_000_000_000);
                if (sample == null)
                    throw new InvalidOperationException($"No camera frame (MJPEG={jpeg})");
                using var caps = sample.GetCaps();
                if (caps?.ToString().Contains("video/x-raw") != true)
                    throw new InvalidOperationException("Camera output is not raw video");
                Console.WriteLine($"Camera bridge passed (MJPEG={jpeg}): {caps}");
            }
            finally
            {
                pipeline.SetState(State.Null);
                camera.Element.Dispose();
            }
        }
    }
}
