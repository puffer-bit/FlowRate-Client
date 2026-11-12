using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// The output the voices of a session are played on. Under PulseAudio or PipeWire it is a
/// pulsesink of its own client name. The voices are silenced by their volume alone: a mute of the
/// stream, which the session manager remembers per client name, outlived the voices it was meant
/// for and silenced the next ones. Each voice stream clears it explicitly when it starts.
/// </summary>
public static class VoicePlaybackSink
{
    private static readonly Lock RoutingLock = new();
    private static readonly HashSet<DeviceBinding> Bindings = [];
    private static string? _selectedDevice;
    private static double _volume = 1;
    private const string VolumeElementName = "output_volume";

    /// <summary>Applies playback gain before compression and echo-reference capture.</summary>
    public static void SetVolume(double volume)
    {
        lock (RoutingLock)
        {
            _volume = double.IsFinite(volume)
                ? Math.Clamp(volume, Client.Core.Settings.AudioSettings.MinOutputVolume,
                    Client.Core.Settings.AudioSettings.MaxOutputVolume) : 1;
            foreach (var binding in Bindings)
                binding.ApplyVolume();
        }
    }

    /// <summary>Routes existing and future playback streams to the selected endpoint.</summary>
    public static void SelectDevice(string? device)
    {
        lock (RoutingLock)
        {
            if (_selectedDevice == device)
                return;
            _selectedDevice = device;
            foreach (var binding in Bindings)
                binding.Apply(true);
        }
    }

    /// <summary>The owner disposes this binding before disposing its pipeline.</summary>
    public static IDisposable BindDevice(Bin branch)
    {
        lock (RoutingLock)
        {
            var binding = new DeviceBinding(branch.GetByName(ElementName), branch.GetByName(VolumeElementName));
            binding.ApplyVolume();
            binding.Apply(false);
            Bindings.Add(binding);
            return binding;
        }
    }

    private sealed class DeviceBinding(Element? sink, Element? volume) : IDisposable
    {
        public void ApplyVolume() => volume?.SetProperty("volume", _volume);

        public void Apply(bool running)
        {
            if (sink?.FindProperty("device") == null)
                return;
            if (!running)
            {
                sink.SetProperty("device", _selectedDevice);
                return;
            }

            // Stop upstream buffers before closing the endpoint so a transient flushing result
            // cannot stop the playout source. Keep the microphone and echo probe running.
            using var input = sink.GetStaticPad("sink");
            using var upstream = input?.GetPeer();
            if (upstream == null)
                return;
            using var idle = new ManualResetEventSlim();
            PadProbeCallback block = (_, _) =>
            {
                idle.Set();
                return PadProbeReturn.Ok;
            };
            var probe = upstream.AddProbe(PadProbeType.Idle, block);
            try
            {
                if (!idle.Wait(TimeSpan.FromSeconds(2)))
                    return;
                sink.SetState(State.Null);
                sink.SetProperty("device", _selectedDevice);
                sink.SyncStateWithParent();
            }
            finally
            {
                upstream.RemoveProbe(probe);
                GC.KeepAlive(block);
            }
        }

        public void Dispose()
        {
            lock (RoutingLock)
            {
                Bindings.Remove(this);
                sink?.Dispose();
                volume?.Dispose();
            }
        }
    }

    /// <summary>Name of the sink element inside a playback branch.</summary>
    public const string ElementName = "output";

    /// <summary>
    /// What the mixed voices go through on their way to the sink, ending with a link. The microphone
    /// test plays the user back through it too.
    /// </summary>
    public const string ShapingDescription =
        "capsfilter caps=\"audio/x-raw,format=F32LE,rate=48000,channels=2\" ! " +
        "volume name=" + VolumeElementName + " ! audiodynamic mode=compressor characteristics=soft-knee threshold=0.7 ratio=0 ! ";

    private const string ClientName = "FlowRate voices";

    // Six requested hardware periods leave room for scheduling jitter.
    private const long BufferTime = AudioDeviceLatency.BufferTimeMicroseconds;
    private const long LatencyTime = AudioDeviceLatency.PeriodTimeMicroseconds;

    private static readonly Lazy<string> SinkDescription = new(Choose);

    /// <summary>The sink fragment, ending the playback description.</summary>
    public static string Description => SinkDescription.Value;

    /// <summary>
    /// Mutes the sink on the sound server. Does nothing for a sink without that property, where
    /// the volume element alone silences the voice.
    /// </summary>
    public static void SetMuted(Bin branch, bool isMuted)
    {
        using var sink = branch.GetByName(ElementName);

        if (sink?.FindProperty("mute") != null)
            sink.SetProperty("mute", isMuted);
    }

    // A pulsesink only works with a sound server to talk to, which reaching READY proves
    private static string Choose()
    {
        // Request a 10 ms period instead of WASAPI's minimum hardware period. The explicit
        // clock pacing in the native bridge bounds read-ahead independently of buffer capacity.
        if (OperatingSystem.IsWindows())
            return $"wasapi2sink name={ElementName} low-latency=false buffer-time={BufferTime} latency-time={LatencyTime}";

        using var probe = ElementFactory.Make("pulsesink", null);

        if (probe == null)
            return Fallback;

        var isReady = probe.SetState(State.Ready) != StateChangeReturn.Failure;
        probe.SetState(State.Null);

        return isReady
            ? $"pulsesink name={ElementName} client-name=\"{ClientName}\" buffer-time={BufferTime} latency-time={LatencyTime}"
            : Fallback;
    }

    private static string Fallback => $"autoaudiosink name={ElementName}";
}
