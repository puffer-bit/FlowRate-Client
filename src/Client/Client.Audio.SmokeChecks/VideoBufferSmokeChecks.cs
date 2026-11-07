using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Application.Ports;
using Client.GStreamer.Interop;
using Client.GStreamer.Video;
using Gst;
using Microsoft.Extensions.Logging.Abstractions;

internal static class VideoBufferSmokeChecks
{
    [DllImport("gstreamer-1.0-0.dll")]
    private static extern IntPtr gst_buffer_peek_memory(IntPtr buffer, uint index);

    public static void Run()
    {
        CheckSharedStorage();
        CheckGate(2, 0, 0, 2);
        CheckGate(60, 12, 0, 1);
        CheckGate(60, 0, 100_000_000, 2);
        CheckEncodingAndDecoding();
        Console.WriteLine("Video buffer smoke checks passed.");
    }

    private static void CheckEncodingAndDecoding()
    {
        var previous = VideoSettings.Current;
        try
        {
            VideoSettings.Apply(previous with { Encoder = "openh264enc", IsHardwareDecodingEnabled = false });
            using var decoder = GstVideoDecoder.Create(VideoCodec.H264, "buffer_smoke", NullLogger.Instance)
                ?? throw new InvalidOperationException("Cannot create software decoder");
            using var capture = GstScreenCapture.Create(new TestPatternVideoInput(),
                new ScreenShareSettings { Framerate = 30, MaxHeight = 480 }, VideoCodec.H264,
                "buffer_smoke", NullLogger.Instance)
                ?? throw new InvalidOperationException("Cannot create software encoder");
            using var decoded = new ManualResetEventSlim();
            decoder.FrameReady += frame =>
            {
                if (frame.Width > 0 && frame.Height > 0) decoded.Set();
            };
            capture.EncodedFrameReady += decoder.Push;
            decoder.KeyFrameNeeded += () => capture.RequestKeyFrame(isUrgent: true);
            Require(capture.Start(), "Start software capture");
            Require(decoded.Wait(TimeSpan.FromSeconds(10)), "Encode and decode synthetic screen through native buffers");
        }
        finally
        {
            VideoSettings.Apply(previous);
        }
    }

    private static void CheckSharedStorage()
    {
        using var input = (Pipeline)Global.ParseLaunch(
            "appsrc name=in is-live=true format=time ! appsink name=out sync=false async=false enable-last-sample=false");
        using var output = (Pipeline)Global.ParseLaunch(
            "appsrc name=in is-live=true format=time ! appsink name=out sync=false async=false enable-last-sample=false");
        using var source = output.GetByName("in")!;
        using var inputSource = input.GetByName("in")!;
        using var inputSink = input.GetByName("out")!;
        using var outputSink = output.GetByName("out")!;
        try
        {
            input.SetState(State.Playing);
            output.SetState(State.Playing);
            var data = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, data, 8);
                Require(GstEncodedNative.Push(inputSource, new EncodedVideoFrame(data, 8, 0, true)), "Push foreign frame");
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
            IntPtr memory;
            byte firstByte = 0;
            using (var sample = GstApp.TryPullSample(inputSink, 2_000_000_000)
                   ?? throw new InvalidOperationException("Synthetic sample did not arrive"))
            using (var buffer = sample.GetBuffer()!)
            {
                memory = gst_buffer_peek_memory(buffer.Handle, 0);
                Require(GstEncodedNative.Read(buffer, frame =>
                {
                    firstByte = Marshal.ReadByte(frame.Data);
                    Require(GstEncodedNative.Push(source, frame with { Timestamp = 123_000_000 }), "Push shared frame");
                }), "Map synthetic frame");
                Require(GstEncodedNative.Read(buffer, frame =>
                    Require(frame.Timestamp != 123_000_000, "Source timestamp was not modified")), "Read source again");
            }

            // The destination must keep the payload alive after the source sample is released.
            using var received = GstApp.TryPullSample(outputSink, 2_000_000_000)
                ?? throw new InvalidOperationException("Shared frame did not arrive");
            using var copied = received.GetBuffer()!;
            Require(gst_buffer_peek_memory(copied.Handle, 0) == memory, "Payload memory is shared");
            Require(GstEncodedNative.Read(copied, frame =>
            {
                Require(frame.Timestamp == 123_000_000, "Destination timestamp");
                Require(Marshal.ReadByte(frame.Data) == firstByte, "Payload survived source release");
            }), "Read destination");
        }
        finally
        {
            input.SetState(State.Null);
            output.SetState(State.Null);
        }
    }

    private static void CheckGate(ulong frames, ulong bytes, ulong time, int accepted)
    {
        using var pipeline = (Pipeline)Global.ParseLaunch(
            "appsrc name=in is-live=true format=time max-buffers=0 max-bytes=0 ! fakesink async=false");
        using var source = pipeline.GetByName("in")!;
        var data = Marshal.AllocHGlobal(8);
        try
        {
            // A live source in PAUSED accepts buffers without draining them.
            pipeline.SetState(State.Paused);
            var gate = new EncodedFrameGate(source, frames, bytes, time);
            for (var i = 0; i < accepted; i++)
                Require(!gate.Push(new EncodedVideoFrame(data, 8, i * 100_000_000L, i == 0)), "Accept frame within budget");
            Require(gate.Push(new EncodedVideoFrame(data, 8, accepted * 100_000_000L, false)), "Overflow requests keyframe");
            Require(gate.Dropped == 1, "Overflow counted");
            gate.Push(new EncodedVideoFrame(data, 8, (accepted + 1) * 100_000_000L, false));
            Require(gate.Dropped == 2, "Dependent frames dropped until keyframe");
            Require(GstNative.GetUInt64(source.Handle, "current-level-buffers") == (ulong)accepted, "Queue stays bounded");

            // Draining does not make dependent frames safe; a new keyframe restores delivery.
            pipeline.SetState(State.Null);
            pipeline.SetState(State.Paused);
            gate.Push(new EncodedVideoFrame(data, 8, 0, true));
            Require(GstNative.GetUInt64(source.Handle, "current-level-buffers") == 1, "Keyframe restores delivery");
        }
        finally
        {
            pipeline.SetState(State.Null);
            Marshal.FreeHGlobal(data);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
