using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Application.Ports;
using Client.GStreamer.Interop;
using Client.GStreamer.Video;
using Gst;
using Microsoft.Extensions.Logging.Abstractions;
using Client.WebRTC.Sdp;

internal static class VideoBufferSmokeChecks
{
    [DllImport("gstreamer-1.0-0.dll")]
    private static extern IntPtr gst_buffer_peek_memory(IntPtr buffer, uint index);

    public static void Run()
    {
        CodecSettingsSmokeChecks.Run();
        CheckSharedStorage();
        CheckGate(2, 0, 0, 2);
        CheckGate(60, 12, 0, 1);
        CheckGate(60, 0, 100_000_000, 2);
        CheckEncodingAndDecoding(VideoCodec.H264, "openh264enc");
        CheckEncodingAndDecoding(VideoCodec.VP8, "vp8enc");
        CheckEncodingAndDecoding(VideoCodec.VP9, "vp9enc");
        CheckEncodingAndDecoding(VideoCodec.AV1, "svtav1enc");
        CheckRtpRoundTrip(VideoCodec.H264, "openh264enc bitrate=1000000", "openh264dec");
        CheckRtpRoundTrip(VideoCodec.AV1,
            "svtav1enc target-bitrate=1000 preset=12 parameters-string=pred-struct=1:rc=2:lookahead=0", "dav1ddec");
        Console.WriteLine("Video buffer smoke checks passed.");
    }

    private static void CheckRtpRoundTrip(VideoCodec codec, string encoder, string decoder)
    {
        Console.WriteLine($"Checking {codec} RTP round trip...");
        var format = VideoCodecPipeline.For(codec);
        using var pipeline = (Pipeline)Global.ParseLaunch(
            "appsrc name=raw is-live=true format=time do-timestamp=true " +
            "caps=\"video/x-raw,format=BGRx,width=320,height=240,framerate=30/1\" ! videoconvert ! " +
            encoder + " ! " + format.Capture + " ! " + format.Payloader + " name=packetizer pt=104 ssrc=1234 " +
            "seqnum-offset=42 timestamp-offset=1234 mtu=1200 " + format.PayloaderOptions + " ! " +
            format.Depayloader + " request-keyframe=true wait-for-keyframe=true ! " + format.Parse +
            format.Caps + " ! " + decoder + " ! appsink name=decoded sync=false");
        using var sink = pipeline.GetByName("decoded")!;
        using var source = pipeline.GetByName("raw")!;
        if (codec == VideoCodec.AV1)
        {
            using var payloader = pipeline.GetByName("packetizer")!;
            Require(GstPacketTransform.Attach(payloader, "src", new Av1RtpDependencyDescriptor().Rewrite),
                "Attach AV1 dependency descriptors");
        }
        try
        {
            Require(pipeline.SetState(State.Playing) != StateChangeReturn.Failure, $"Start {codec} RTP pipeline");
            for (var index = 0; index < 30; index++)
            {
                using var buffer = Gst.Buffer.NewMemdup(new byte[320 * 240 * 4]);
                GstApp.PushBuffer(source, buffer);
            }
            using var sample = GstApp.TryPullSample(sink, 15_000_000_000);
            Require(sample != null, $"Decode {codec} after RTP packetization and reassembly");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    private static void CheckEncodingAndDecoding(VideoCodec codec, string encoder)
    {
        Console.WriteLine($"Checking {codec} encoding and decoding with {encoder}...");
        var previous = VideoSettings.Current;
        try
        {
            var softwareDecoder = codec switch
            {
                VideoCodec.H264 => "openh264dec",
                VideoCodec.AV1 => "dav1ddec",
                VideoCodec.VP8 => "vp8dec",
                VideoCodec.VP9 => "vp9dec",
                _ => throw new ArgumentOutOfRangeException(nameof(codec))
            };
            VideoSettings.Apply(new VideoSettings
            {
                Encoder = encoder, Av1Encoder = encoder, Vp8Encoder = encoder, Vp9Encoder = encoder,
                IsHardwareDecodingEnabled = false,
                CodecOverrides = [new(softwareDecoder, "max-errors", "7")]
            });
            using var decoder = GstVideoDecoder.Create(codec, "buffer_smoke", NullLogger.Instance)
                ?? throw new InvalidOperationException("Cannot create software decoder");
            using var capture = GstScreenCapture.Create(new TestPatternVideoInput(),
                new ScreenShareSettings { Framerate = 30, MaxHeight = 480 }, codec,
                "buffer_smoke", NullLogger.Instance)
                ?? throw new InvalidOperationException("Cannot create software encoder");
            using var decoded = new ManualResetEventSlim();
            using var requestedKeyframe = new ManualResetEventSlim();
            var waitingForKeyframe = 0;
            decoder.FrameReady += frame =>
            {
                if (frame.Width > 0 && frame.Height > 0) decoded.Set();
            };
            capture.EncodedFrameReady += frame =>
            {
                decoder.Push(frame);
                if (Volatile.Read(ref waitingForKeyframe) != 0 && frame.IsKeyFrame)
                    requestedKeyframe.Set();
            };
            decoder.KeyFrameNeeded += () => capture.RequestKeyFrame(isUrgent: true);
            Require(capture.Start(), "Start software capture");
            Require(decoded.Wait(TimeSpan.FromSeconds(15)), $"Encode and decode {codec} screen through native buffers");
            Volatile.Write(ref waitingForKeyframe, 1);
            capture.RequestKeyFrame(isUrgent: true);
            Require(requestedKeyframe.Wait(TimeSpan.FromSeconds(2)), $"{codec} responds to a viewer's keyframe request");
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
