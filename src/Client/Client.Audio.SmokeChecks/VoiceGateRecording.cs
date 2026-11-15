using Client.Application.Ports;
using Client.Core.Settings;
using Client.GStreamer.Audio;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plays a prepared PCM file (s16le, 48 kHz, mono) through the processed microphone, gate on, and
/// writes the peak of every 10 ms frame that leaves it, one per line: a zero is a frame the gate held
/// back. Compared with where the file has its speech and its noises, it shows what the gate lets out.
/// </summary>
/// <remarks>
/// Usage: --voice-gate-recording &lt;pcm&gt; &lt;out&gt; &lt;seconds&gt; &lt;threshold dB&gt; [voice] [adaptive]
/// [suppression] [gain].
/// </remarks>
internal static class VoiceGateRecording
{
    public static async Task Run(GstAudioDeviceProvider devices, string[] args)
    {
        using var audio = new GstVoiceAudioDevice(devices, new ConsoleLogger<GstVoiceAudioDevice>())
        {
            PlaybackSink = "fakesink name=output sync=true async=false"
        };
        var at = Array.IndexOf(args, "--voice-gate-recording");
        var pcm = args[at + 1];
        var output = args[at + 2];
        var seconds = double.Parse(args[at + 3], System.Globalization.CultureInfo.InvariantCulture);
        var threshold = double.Parse(args[at + 4], System.Globalization.CultureInfo.InvariantCulture);
        var flags = args.Skip(at + 5).ToHashSet();
        var suppression = flags.FirstOrDefault(flag => Enum.TryParse<NoiseSuppressionLevel>(flag, out _)) is { } name
            ? Enum.Parse<NoiseSuppressionLevel>(name)
            : NoiseSuppressionLevel.Moderate;

        if (!audio.StartPlayback(samples => samples.Clear()))
            throw new InvalidOperationException("Cannot start playback");

        var peaks = new List<int>();
        using var microphone = audio.CreateMicrophone(new FileAudioInput(pcm), 1, suppression,
            new VoiceGateOptions(threshold, flags.Contains("voice"), flags.Contains("adaptive")), false,
            flags.Contains("gain"))!;

        microphone.SamplesReady += samples =>
        {
            var peak = 0;
            foreach (var sample in samples)
                peak = Math.Max(peak, Math.Abs((int)sample));
            lock (peaks)
                peaks.Add(peak);
        };

        if (!microphone.Start())
            throw new InvalidOperationException("Cannot start the microphone");

        for (var elapsed = 0; elapsed < seconds; elapsed += 10)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, seconds - elapsed)));
            if (microphone.TakeGateSummary() is { } summary)
                Console.WriteLine(summary);
        }

        string[] lines;
        lock (peaks)
            lines = peaks.Select(peak => peak.ToString()).ToArray();
        await File.WriteAllLinesAsync(output, lines);

        audio.StopPlayback();
    }

    private sealed class ConsoleLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Console.WriteLine($"{logLevel}: {formatter(state, exception)} {exception}");
        }
    }
}
