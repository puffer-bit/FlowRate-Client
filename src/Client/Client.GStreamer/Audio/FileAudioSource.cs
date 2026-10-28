using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Gst;
using Client.Application.Ports;

namespace Client.GStreamer.Audio;

/// <summary>
/// Plays audio files as a live source that loops forever.
/// </summary>
/// <remarks>
/// A file is decoded once into raw 48 kHz mono PCM. Looping a raw file is the only way that keeps the
/// timestamps continuous: parsers of container formats end the stream after the first pass, and
/// seeking back would reset the running time downstream. <c>multifilesrc</c> hands over the whole
/// file in one buffer, so the stream is cut into 20 ms frames and paced by the clock before it
/// reaches the encoder.
/// </remarks>
public static class FileAudioSource
{
    public const int SampleRate = 48000;

    private const string PlaybackDescription =
        "multifilesrc name=file loop=true ! " +
        "rawaudioparse use-sink-caps=false format=pcm pcm-format=s16le sample-rate=48000 num-channels=1 ! " +
        "audiobuffersplit output-buffer-duration=1/50 ! clocksync";

    private const string ToneDescription = "audiotestsrc is-live=true wave=sine freq={0} volume=0.25";

    /// <summary>
    /// Decodes <paramref name="path"/> into raw 48 kHz mono S16LE audio next to the temp directory.
    /// </summary>
    /// <param name="rawSampleRate">
    /// Sample rate of a raw 16-bit PCM input, which carries no header. Null for files GStreamer can
    /// recognize by itself (wav, mp3, ogg, flac and so on).
    /// </param>
    /// <param name="rawChannels">Channel count of a raw input.</param>
    /// <returns>Path of the prepared file.</returns>
    public static Task<string> PrepareAsync(string path, int? rawSampleRate = null, int rawChannels = 1,
        CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.Run(() => Prepare(path, rawSampleRate, rawChannels), cancellationToken);
    }

    private static string Prepare(string path, int? rawSampleRate, int rawChannels)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Audio file not found", fullPath);

        var info = new FileInfo(fullPath);
        var key = $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{rawSampleRate}|{rawChannels}";
        // string.GetHashCode changes between runs, which would defeat the cache
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        var output = Path.Combine(Path.GetTempPath(), $"flowrate-audio-{hash}.pcm");

        // Decoding a long file takes a moment, so the result is kept for the next run
        if (File.Exists(output) && new FileInfo(output).Length > 0)
            return output;

        var decoder = rawSampleRate.HasValue
            ? string.Format(CultureInfo.InvariantCulture,
                "rawaudioparse use-sink-caps=false format=pcm pcm-format=s16le sample-rate={0} num-channels={1}",
                rawSampleRate.Value, rawChannels)
            : "decodebin";

        var description = $"filesrc name=file ! {decoder} ! audioconvert ! audioresample ! " +
                          $"audio/x-raw,format=S16LE,rate={SampleRate},channels=1 ! filesink name=output";

        var temporary = output + ".part";

        using var pipeline = (Pipeline)Global.ParseLaunch(description);
        using (var file = pipeline.GetByName("file"))
            file?.SetProperty("location", fullPath);

        using (var sink = pipeline.GetByName("output"))
            sink?.SetProperty("location", temporary);

        pipeline.SetState(State.Playing);

        using var bus = pipeline.GetBus();
        using var message = bus.TimedPopFiltered(ClockTime.None,  MessageType.Eos | MessageType.Error);
        var failed = message == null || message.Type == MessageType.Error;

        pipeline.SetState(State.Null);

        if (failed || !File.Exists(temporary) || new FileInfo(temporary).Length == 0)
        {
            File.Delete(temporary);
            throw new InvalidOperationException(rawSampleRate.HasValue
                ? $"Failed to read {fullPath} as raw PCM"
                : $"Failed to decode {fullPath}. For raw PCM pass its sample rate");
        }

        File.Move(temporary, output, true);

        return output;
    }

    /// <summary>
    /// Creates the source element for an input that is not a capture device.
    /// </summary>
    /// <returns>An element with a single src pad of raw audio, or null when an element is missing.</returns>
    public static Element? CreateElement(AudioInput input)
    {
        switch (input)
        {
            case FileAudioInput file:
            {
                var bin = Global.ParseBinFromDescription(PlaybackDescription, true);

                if (bin == null)
                    return null;

                using (var source = bin.GetByName("file"))
                    source?.SetProperty("location", file.PreparedPcmPath);

                return bin;
            }

            case ToneAudioInput tone:
                return Global.ParseBinFromDescription(
                    string.Format(CultureInfo.InvariantCulture, ToneDescription, tone.Frequency), true);

            default:
                return null;
        }
    }
}
