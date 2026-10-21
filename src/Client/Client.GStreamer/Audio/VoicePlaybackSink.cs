using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// The output the voices of a session are played on. Under PulseAudio or PipeWire it is a
/// pulsesink of its own client name, so silencing the voices can mute the stream on the sound
/// server, which also silences what is already buffered there and takes effect at once. The
/// session manager remembers that mute per client name, so a name of their own keeps it away from
/// the other sounds of the app; each voice stream sets it explicitly when it starts.
/// </summary>
internal static class VoicePlaybackSink
{
    /// <summary>Name of the sink element inside a playback branch.</summary>
    public const string ElementName = "output";

    private const string ClientName = "FlowRate voices";

    // The voice already past its volume still plays for this long, in microseconds: the default
    // of 200 ms let a muted user be heard. Much less underruns on a busy machine and crackles
    private const long BufferTime = 60_000;
    private const long LatencyTime = 10_000;

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
