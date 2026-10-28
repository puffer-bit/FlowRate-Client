using System.Diagnostics;
using Client.Application.Ports;

namespace Client.LibWebRTC.Native;

/// <summary>
/// Plays a tone or a prepared file into a custom source of libwebrtc, 10 ms at a time and on time,
/// on a thread of its own: what the microphone of the test bot is.
/// </summary>
internal sealed class CustomAudioFeeder : IDisposable
{
    public const int SampleRate = 48000;
    private const int FrameSamples = SampleRate / 100;
    private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(10);

    // A tone at a level that leaves room for several of them in a mix
    private const double ToneAmplitude = 0.25;

    private readonly Action<short[]> _deliver;
    private readonly short[] _pcm;
    private readonly double _frequency;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private long _position;
    private volatile float _volume = 1;

    /// <returns>The feeder, or null for an input that is not a tone or a file.</returns>
    public static CustomAudioFeeder? For(AudioInput input, Action<short[]> deliver) => input switch
    {
        ToneAudioInput tone => new CustomAudioFeeder(deliver, [], tone.Frequency),
        FileAudioInput file => new CustomAudioFeeder(deliver, ReadPcm(file.PreparedPcmPath), 0),
        _ => null
    };

    private CustomAudioFeeder(Action<short[]> deliver, short[] pcm, double frequency)
    {
        _deliver = deliver;
        _pcm = pcm;
        _frequency = frequency;
        _thread = new Thread(Run) { IsBackground = true, Name = "libwebrtc custom audio" };
    }

    /// <summary>Starts playing, once the peer takes the custom source: frames before are dropped.</summary>
    public void Start() => _thread.Start();

    /// <summary>The gain of what is played, 1 keeps it as it is.</summary>
    public double Volume
    {
        set => _volume = (float)value;
    }

    private void Run()
    {
        var frame = new short[FrameSamples];
        var start = Stopwatch.GetTimestamp();

        for (long index = 0; !_stop.IsCancellationRequested; index++)
        {
            Fill(frame);
            _deliver(frame);

            // Paced by the clock, not by the sleeps, so the stream neither drifts nor bunches up
            var due = FrameTime * (index + 1) - Stopwatch.GetElapsedTime(start);

            if (due > TimeSpan.Zero)
                _stop.Token.WaitHandle.WaitOne(due);
        }
    }

    private void Fill(short[] frame)
    {
        var volume = _volume;

        for (var i = 0; i < frame.Length; i++, _position++)
        {
            double sample = _pcm.Length > 0
                ? _pcm[_position % _pcm.Length]
                : Math.Sin(2 * Math.PI * _frequency * _position / SampleRate) * ToneAmplitude * short.MaxValue;

            frame[i] = (short)Math.Clamp(sample * volume, short.MinValue, short.MaxValue);
        }
    }

    // Raw 48 kHz mono S16LE, as FileAudioInput promises
    private static short[] ReadPcm(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);

        return samples;
    }

    public void Dispose()
    {
        _stop.Cancel();

        if (_thread.ThreadState != System.Threading.ThreadState.Unstarted)
            _thread.Join();

        _stop.Dispose();
    }
}
