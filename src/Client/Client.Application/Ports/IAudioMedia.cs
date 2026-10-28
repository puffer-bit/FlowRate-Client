namespace Client.Application.Ports;

/// <summary>
/// Sound captured as samples, for a transport that encodes it itself: the sound of a shown screen
/// over libwebrtc. Implemented by the media engine.
/// </summary>
public interface IAudioMedia
{
    /// <summary>
    /// Prepares the capture of an input other than a microphone. Nothing is captured before
    /// <see cref="IAudioCapture.Start"/>.
    /// </summary>
    /// <param name="name">Tells the capture apart in the log and names its threads.</param>
    /// <returns>Null when the input cannot be captured here.</returns>
    IAudioCapture? CreateCapture(AudioInput input, string name);
}

/// <summary>The samples of a capture, only valid during the call.</summary>
public delegate void AudioSamplesHandler(ReadOnlySpan<short> samples);

/// <summary>
/// The capture of one sound, in 16-bit samples at <see cref="SampleRate"/>, <see cref="Channels"/>
/// interleaved, in pieces of any length. Raised on a thread of the capture; subscribe before
/// <see cref="Start"/>.
/// </summary>
public interface IAudioCapture : IDisposable
{
    const int SampleRate = 48000;
    const int Channels = 2;

    event AudioSamplesHandler? SamplesReady;

    /// <returns>False when the capture could not start.</returns>
    bool Start();
}
