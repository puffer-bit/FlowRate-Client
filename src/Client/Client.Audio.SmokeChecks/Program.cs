using System.Diagnostics;
using Client.Application.Ports;
using Client.Core.Settings;
using Client.GStreamer.Audio;
using Client.GStreamer.Loader;
using Client.LibWebRTC.Native;
using Client.LibWebRTC.Voice;
using Microsoft.Extensions.Logging.Abstractions;

// Requires the bundled media runtime and matching native prebuilt. Uses synthetic inputs and a
// clocked fake output, so these checks never open a microphone or play sound on the speakers.
GstNativeLibraryResolver.Register();
if (!GstLoader.LoadGstreamer(NullLogger.Instance))
    throw new InvalidOperationException("Cannot load the bundled GStreamer runtime");
if (args.Contains("--process-loopback"))
{
    using var plugin = Gst.ElementFactory.Make("wasapi2src", null);
    var expected = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) &&
                   plugin?.FindProperty("loopback-target-pid") != null &&
                   plugin.FindProperty("loopback-mode") != null;
    Require(ProcessLoopbackSource.IsAvailable == expected, "Process loopback capability matches OS and plugin");
    foreach (var excluded in new[] { false, true })
    {
        using var source = ProcessLoopbackSource.Create(
            new ProcessLoopbackAudioInput((uint)Environment.ProcessId, excluded));
        Require((source != null) == expected, "Unsupported process filtering cannot create device loopback");
        if (source != null)
        {
            using var mode = source.GetProperty("loopback-mode");
            Require(mode.GetEnum() == (excluded ? 2 : 1), "Process filtering mode is applied");
            using var pid = source.GetProperty("loopback-target-pid");
            Require(pid.GetUInt() == (uint)Environment.ProcessId, "Process filtering PID is applied");
        }
    }
    Require(ProcessLoopbackSource.Create(new ProcessLoopbackAudioInput(0, true)) == null,
        "Zero PID cannot create unfiltered loopback");
    return;
}
if (args.Contains("--video-codec-settings"))
{
    CodecSettingsSmokeChecks.Run();
    Console.WriteLine("Codec settings smoke checks passed.");
    return;
}
if (args.Contains("--video-buffers"))
{
    VideoBufferSmokeChecks.Run();
    return;
}
if (args.Contains("--video-av1"))
{
    VideoAv1SmokeChecks.Run();
    return;
}
using var devices = new GstAudioDeviceProvider(NullLogger<GstAudioDeviceProvider>.Instance);
Require(MicrophoneProcessing.HasNeural, "Bundled RNNoise plugin is available");
using var audio = new GstVoiceAudioDevice(devices, NullLogger<GstVoiceAudioDevice>.Instance)
{
    PlaybackSink = "fakesink name=output sync=true async=false"
};
string? audioError = null;
audio.Failed += error => Interlocked.CompareExchange(ref audioError, error, null);

if (args.Contains("--video-negotiation"))
{
    var negotiationRuntime = new LibWebRTCRuntime(audio, NullLogger<LibWebRTCRuntime>.Instance);
    Require(negotiationRuntime.TryLoad(), negotiationRuntime.Load() ?? "Load native runtime");
    await VideoNegotiationSmokeChecks.Run(negotiationRuntime);
    return;
}

var pulls = 0;
var stall = 0;
Require(audio.StartPlayback(samples =>
{
    Require(samples.Length == 960, "Playout frame size");
    samples.Clear();
    Interlocked.Increment(ref pulls);
    if (Interlocked.Exchange(ref stall, 0) != 0)
        Thread.Sleep(300);
}), "Start clocked playback");
await Task.Delay(200);
var before = Volatile.Read(ref pulls);
await Task.Delay(1000);
var count = Volatile.Read(ref pulls) - before;
Require(count is >= 85 and <= 115, $"Hardware-paced playout: {count} frames/s");
Interlocked.Exchange(ref stall, 1);
before = Volatile.Read(ref pulls);
await Task.Delay(1000);
count = Volatile.Read(ref pulls) - before;
Require(count is >= 50 and <= 90, $"No catch-up burst after output stall: {count} frames");

using (var first = audio.CreateMicrophone(new ToneAudioInput(440), 1, NoiseSuppressionLevel.Off, null, false, false)!)
{
    var frames = 0;
    first.SamplesReady += samples =>
    {
        Require(samples.Length == 480, $"Capture frame size: {samples.Length}");
        Interlocked.Increment(ref frames);
    };
    Require(first.Start(), "Start processed microphone");
    await Until(() => Volatile.Read(ref frames) >= 20, "Processed PCM reaches the transport");
    Require(first.TakeLevel() > 0.1, "Tone is audible with suppression off");
    first.SetVolume(0);
    await Task.Delay(200);
    first.TakeLevel();
    await Task.Delay(150);
    Require(first.TakeLevel() == 0, "Input volume applies once");
    first.SetVolume(1);
    first.SetVoiceGate(new VoiceGateOptions(0));
    await Task.Delay(600);
    first.TakeLevel();
    await Task.Delay(150);
    Require(first.TakeLevel() == 0, "Voice gate closes on a quiet input");
    first.SetVoiceGate(null);
    await Task.Delay(200);
    Require(first.TakeLevel() > 0.1, "Disabling the gate restores PCM");

    foreach (var suppression in new[] { NoiseSuppressionLevel.Neural, NoiseSuppressionLevel.NeuralAndWebRtc })
    {
        using var second = audio.CreateMicrophone(new ToneAudioInput(440), 1, suppression,
            new VoiceGateOptions(-50, true, true), true, true)!;
        var secondFrames = 0;
        second.SamplesReady += _ => Interlocked.Increment(ref secondFrames);
        Require(second.Start(), $"Start {suppression} with echo probe and voice detection");
        await Until(() => Volatile.Read(ref secondFrames) >= 20, $"Receive {suppression} frames");
        var oldFrames = Volatile.Read(ref frames);
        await Task.Delay(150);
        Require(Volatile.Read(ref frames) > oldFrames, "Shared microphone still feeds the first peer");
    }
    var restored = Volatile.Read(ref frames);
    await Until(() => Volatile.Read(ref frames) > restored + 10, "Closing the current owner restores its predecessor");
}
audio.StopPlayback();

// Exercise the production native peer and its custom source, including asynchronous teardown and
// restart of the shared audio device. No server or ICE connection is required for this boundary.
var runtime = new LibWebRTCRuntime(audio, NullLogger<LibWebRTCRuntime>.Instance);
Require(runtime.TryLoad(), runtime.Load() ?? "Load native runtime");
for (var round = 0; round < 3; round++)
{
    var listener = new Listener();
    using (var peer = new NativeVoicePeer(runtime, listener))
    {
        Require(peer.SetMicrophone(new ToneAudioInput(440), NoiseSuppressionLevel.Off, false, false), "Configure native peer");
        await Task.Delay(250);
        peer.RequestStats(false);
        await Until(() => listener.Stats != null, "Native stats callback");
        Require(listener.Stats!.Levels.Any(level => level.Mid == null && level.Level > 0.1), "Custom capture speaking level");
        using (var second = new NativeVoicePeer(runtime, new Listener()))
        {
            Require(second.SetMicrophone(new ToneAudioInput(440), NoiseSuppressionLevel.Off, false, false), "Share native capture");
            second.SetVoiceGate(new VoiceGateOptions(0));
            await Task.Delay(300);
            second.StopMicrophone();
            await Task.Delay(200);
            listener.Stats = null;
            peer.RequestStats(false);
            await Until(() => listener.Stats != null, "Stats after stopping another peer's microphone");
            Require(listener.Stats!.Levels.Any(level => level.Mid == null && level.Level > 0.1),
                "Stopping a microphone restores the other peer's gate without closing its connection");
            await Task.Delay(100);
        }
        await Task.Delay(150);
        Require(audio.IsRunning, "Releasing one peer keeps playback alive");
        Require(listener.Error == null, listener.Error ?? "Native peer errors");
    }
    await Until(() => !audio.IsRunning, "Releasing the last native peer stops the shared device");
}
await Task.Delay(500);
Require(audioError == null, audioError ?? "Audio pipeline has no errors");
Console.WriteLine("PASS: clock pacing, bounded stall recovery, 10 ms capture, volume, gate, RNNoise, echo probe, shared ownership and native peer restart");

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task Until(Func<bool> condition, string message)
{
    var started = Stopwatch.GetTimestamp();
    while (!condition())
    {
        Require(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5), message);
        await Task.Delay(20);
    }
}

sealed class Listener : IVoicePeerListener
{
    public volatile VoiceStats? Stats;
    public volatile string? Error;
    public void OnStats(VoiceStats stats) => Stats = stats;
    public void OnFailed(string error) => Error = error;
    public void OnConnectionChanged(WebRTCConnectionState state) { }
    public void OnNegotiated(string localSdp) { }
}
