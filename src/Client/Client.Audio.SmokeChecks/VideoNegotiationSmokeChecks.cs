using System.Collections.Concurrent;
using System.Diagnostics;
using Client.Application.Ports;
using Client.Core.Sessions.Hybrid;
using Client.Core.Media;
using Client.LibWebRTC.Native;
using Client.LibWebRTC.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models.Media;

internal static class VideoNegotiationSmokeChecks
{
    // Uses the actual native answerer: an offer containing NACK is not enough if a reused
    // transceiver retains the feedback negotiated for its previous track. No SFU is contacted.
    public static async Task Run(LibWebRTCRuntime runtime)
    {
        var answers = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        using var transport = new VoiceTransport(1,
            listener => new NativeVoicePeer(runtime, new Listener(listener, answers, errors)),
            NullLogger<VoiceTransport>.Instance, isPlaybackEnabled: false, video: new SilentVideoMedia(),
            isVideoDecodedByPeer: true);
        var parameters = new TransportParameters("127.0.0.1", 9,
            new DtlsParameters("auto", [new DtlsFingerprint("sha-256", string.Join(':', Enumerable.Repeat("00", 32)))]),
            new IceParameters("smoke", "smoke-test-password-1234567890", []));
        Require(transport.Connect(parameters), "Connect negotiation smoke peer");
        await AnswerAfter(() => { }, 1);

        var disabled = Track("without-nack", 101, false);
        var enabled = Track("with-nack", 102, true);
        await AnswerAfter(() => transport.RegisterInbound(disabled, 1));
        Require(!HasVideoNack(answers.Last()), "NACK is disabled for the first track");
        await AnswerAfter(() => transport.UnregisterInbound(2, MediaType.Video, MediaSourceType.Screen));
        await AnswerAfter(() => transport.RegisterInbound(enabled, 1));
        Require(HasVideoNack(answers.Last()), "Native answer enables NACK after replacing a disabled track");
        var firstEnabledAnswer = answers.Last();
        var count = firstEnabledAnswer.Split("m=video ").Length - 1;

        await AnswerAfter(() => transport.UnregisterInbound(2, MediaType.Video, MediaSourceType.Screen));
        await AnswerAfter(() => transport.RegisterInbound(enabled with { Id = "with-nack-again", SSRC = 103 }, 1));
        Require(HasVideoNack(answers.Last()), "NACK stays enabled when a compatible slot is reused");
        Require(answers.Last().Split("m=video ").Length - 1 == count,
            "Replacing a track with the same feedback reuses its video section");

        await AnswerAfter(() => transport.UnregisterInbound(2, MediaType.Video, MediaSourceType.Screen));
        await AnswerAfter(() => transport.RegisterInbound(disabled with { Id = "without-nack-again", SSRC = 104 }, 1));
        Require(!HasVideoNack(answers.Last()), "A disabled replacement does not inherit NACK");
        await AnswerAfter(() => transport.UnregisterInbound(2, MediaType.Video, MediaSourceType.Screen));
        await AnswerAfter(() => transport.RegisterInbound(enabled with { Id = "with-nack-third", SSRC = 105 }, 1));
        Require(HasVideoNack(answers.Last()), "NACK can be enabled again without reconnecting");
        Require(answers.Last().Split("m=video ").Length - 1 == count,
            "Feedback changes reuse compatible historical slots instead of growing sections indefinitely");
        Console.WriteLine("PASS: native video NACK negotiation across track replacements and compatible section reuse");

        await AnswerAfter(() => transport.UnregisterInbound(2, MediaType.Video, MediaSourceType.Screen));
        await AnswerAfter(() => transport.RegisterInbound(enabled with
        {
            Id = "av1", SSRC = 106, MimeType = "video/AV1", PayloadType = 104
        }, 1));
        Require(answers.Last().Split("m=").Any(section => section.StartsWith("video ") &&
            section.Contains("a=recvonly\r\n") && section.Contains("a=rtpmap:104 AV1/90000\r\n")),
            "Native answer accepts AV1 after replacing an H.264 track");
        Console.WriteLine("PASS: native AV1 receive negotiation");

        // No capture device or SFU is needed to verify the send transceivers and paused SSRCs.
        var outgoing = new OutboundTrack
        {
            Id = "send-without-nack", UserId = 1, SSRC = 201, SourceType = MediaSourceType.Screen,
            MediaType = MediaType.Video, MimeType = "video/H264", PayloadType = 102, ClockRate = 90000
        };
        await AnswerAfter(() => Require(transport.StartOutbound(outgoing, new TestPatternVideoInput(),
            new ScreenShareSettings()), "Start silent screen source"));
        Require(!HasSendNack(answers.Last()), "First send section has NACK disabled");
        await AnswerAfter(() => transport.StopOutbound(outgoing.Id));
        var withRtx = outgoing with { Id = "send-with-nack", SSRC = 202, RtxSSRC = 302, RtxPayloadType = 103 };
        await AnswerAfter(() => Require(transport.StartOutbound(withRtx, new TestPatternVideoInput(),
            new ScreenShareSettings()), "Replace screen producer inside the same transport"));
        Require(HasSendNack(answers.Last()), "Send answer enables NACK after replacing a disabled producer");
        Require(!answers.Last().Split("m=").Any(section => section.Contains("a=sendonly\r\n") &&
            section.Contains("a=ssrc:201 ")), "Replaced producer no longer sends its paused SSRC");
        await AnswerAfter(() => transport.StopOutbound(withRtx.Id));
        await AnswerAfter(() => Require(transport.StartOutbound(outgoing with { Id = "send-disabled-again", SSRC = 203 },
            new TestPatternVideoInput(), new ScreenShareSettings()), "Replace enabled producer"));
        Require(!HasSendNack(answers.Last()), "Send answer disables NACK without reconnecting");
        Console.WriteLine("PASS: native send NACK changes and paused producer replacement without reconnecting");

        async Task AnswerAfter(Action action, int? answerCount = null)
        {
            var expected = answerCount ?? answers.Count + 1;
            action();
            var started = Stopwatch.GetTimestamp();
            while (answers.Count < expected && errors.IsEmpty && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
                await Task.Delay(10);
            Require(errors.IsEmpty, string.Join("; ", errors));
            Require(answers.Count >= expected, "Native negotiation completed");
        }
    }

    private static InboundTrack Track(string id, uint ssrc, bool nack) => new()
    {
        Id = id, UserId = 2, SSRC = ssrc, SourceType = MediaSourceType.Screen, MediaType = MediaType.Video,
        MimeType = "video/H264", PayloadType = 102, ClockRate = 90000, IsNackEnabled = nack
    };

    private static bool HasVideoNack(string answer) => answer.Split("m=")
        .Where(section => section.StartsWith("video ") && section.Contains("a=recvonly\r\n"))
        .Any(section => section.Contains("a=rtcp-fb:102 nack\r\n"));

    private static bool HasSendNack(string answer) => answer.Split("m=")
        .Where(section => section.StartsWith("video ") && section.Contains("a=sendonly\r\n"))
        .Any(section => section.Contains("a=rtcp-fb:102 nack\r\n"));

    private sealed class SilentVideoMedia : IVideoMedia
    {
        public IScreenCapture CreateCapture(VideoInput input, ScreenShareSettings settings, VideoCodec codec, string name)
            => new SilentCapture();
        public IVideoDecoder? CreateDecoder(VideoCodec codec, string name) => null;
    }

    private sealed class SilentCapture : IScreenCapture
    {
        public event Action<Exception>? Failed { add { } remove { } }

        public string Description => "Negotiation check without captured frames";
        public event Action<RawVideoFrame>? PreviewFrameReady { add { } remove { } }
        public event Action<EncodedVideoFrame>? EncodedFrameReady { add { } remove { } }
        public bool Start() => true;
        public void RequestKeyFrame(bool isUrgent = false) { }
        public void SetBitrate(int kbps) { }
        public void SetPreviewShown(bool isShown) { }
        public void Dispose() { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Listener(IVoicePeerListener inner, ConcurrentQueue<string> answers,
        ConcurrentQueue<string> errors) : IVoicePeerListener
    {
        public void OnNegotiated(string sdp)
        {
            inner.OnNegotiated(sdp);
            answers.Enqueue(sdp);
        }

        public void OnFailed(string error)
        {
            errors.Enqueue(error);
            inner.OnFailed(error);
        }

        public void OnStats(VoiceStats stats) => inner.OnStats(stats);
        public void OnConnectionChanged(WebRTCConnectionState state) => inner.OnConnectionChanged(state);
    }
}
