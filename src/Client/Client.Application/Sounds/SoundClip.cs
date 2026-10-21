namespace Client.Application.Sounds;

/// <summary>
/// A decoded sound: interleaved signed 16-bit little-endian samples. Players keep what they
/// prepared for a clip by its reference, so one instance is reused for every play.
/// </summary>
public sealed class SoundClip
{
    public SoundClip(byte[] pcm, int channels, int sampleRate)
    {
        Pcm = pcm;
        Channels = channels;
        SampleRate = sampleRate;
    }

    public byte[] Pcm { get; }

    /// <summary>
    /// 1 or 2.
    /// </summary>
    public int Channels { get; }

    public int SampleRate { get; }
}
