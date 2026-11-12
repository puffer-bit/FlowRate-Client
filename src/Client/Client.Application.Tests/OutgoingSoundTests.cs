using Client.Application.Ports;
using Client.LibWebRTC.Voice;
using Xunit;

namespace Client.Application.Tests;

public class OutgoingSoundTests
{
    private const int FrameSamples = IAudioCapture.SampleRate / 100 * IAudioCapture.Channels;

    [Theory]
    [InlineData(1)]
    [InlineData(317)]
    [InlineData(FrameSamples)]
    [InlineData(FrameSamples * 3)]
    public void CaptureChunksPreserveSamplesAndTenMillisecondFrames(int chunkSize)
    {
        var capture = new TestCapture();
        var source = new TestSource();
        using var sound = new OutgoingSound(capture, source);
        var samples = Enumerable.Range(0, FrameSamples * 3).Select(i => (short)(i - 1400)).ToArray();

        for (var offset = 0; offset < samples.Length; offset += chunkSize)
            capture.Emit(samples.AsSpan(offset, Math.Min(chunkSize, samples.Length - offset)));

        Assert.Equal(3, source.Frames.Count);
        Assert.All(source.Frames, frame => Assert.Equal(FrameSamples, frame.Length));
        Assert.Equal(samples, source.Frames.SelectMany(frame => frame).ToArray());
    }

    [Fact]
    public void VolumeClipsWithoutChangingCaptureMemory()
    {
        var capture = new TestCapture();
        var source = new TestSource();
        using var sound = new OutgoingSound(capture, source) { Volume = 2 };
        var samples = Enumerable.Range(0, FrameSamples)
            .Select(i => i % 2 == 0 ? (short)20000 : (short)-20000).ToArray();
        var original = samples.ToArray();

        capture.Emit(samples);
        sound.Volume = 1;
        capture.Emit(samples);

        Assert.Equal(original, samples);
        Assert.Equal(original.Select(value => value > 0 ? short.MaxValue : short.MinValue).ToArray(), source.Frames[0]);
        Assert.Equal(original, source.Frames[1]);
    }

    [Fact]
    public void MutingPreservesFrameBoundariesAcrossPartialChunks()
    {
        var capture = new TestCapture();
        var source = new TestSource();
        using var sound = new OutgoingSound(capture, source) { IsMuted = true };

        capture.Emit(new short[FrameSamples * 2 + 100]);
        Assert.Empty(source.Frames);
        sound.IsMuted = false;
        capture.Emit(Enumerable.Repeat((short)123, FrameSamples - 100).ToArray());

        var frame = Assert.Single(source.Frames);
        Assert.All(frame.Take(100), value => Assert.Equal((short)0, value));
        Assert.All(frame.Skip(100), value => Assert.Equal((short)123, value));
    }

    [Fact]
    public void ReplacingCaptureDiscardsItsIncompleteFrame()
    {
        var capture = new TestCapture();
        var source = new TestSource();
        using var sound = new OutgoingSound(capture, source);
        capture.Emit(new short[100]);
        var replacement = new TestCapture();

        Assert.True(sound.Replace(replacement));
        replacement.Emit(Enumerable.Repeat((short)123, FrameSamples).ToArray());

        Assert.All(Assert.Single(source.Frames), value => Assert.Equal((short)123, value));
    }

    [Fact]
    public void ReplaceAfterDisposeStopsTheNewCaptureInsteadOfStartingIt()
    {
        var source = new TestSource();
        var sound = new OutgoingSound(new TestCapture(), source);
        var replacement = new TestCapture();

        sound.Dispose();

        Assert.False(sound.Replace(replacement));
        Assert.False(replacement.IsStarted);
        Assert.True(replacement.IsDisposed);
    }

    [Fact]
    public void ReplaceStopsTheOldCaptureAndStartsTheNewOne()
    {
        var old = new TestCapture();
        var sound = new OutgoingSound(old, new TestSource());
        var replacement = new TestCapture();

        Assert.True(sound.Replace(replacement));
        Assert.True(old.IsDisposed);
        Assert.True(replacement.IsStarted);

        sound.Dispose();
        Assert.True(replacement.IsDisposed);
    }

    [Fact]
    public void DisposeTwiceDisposesTheSourceOnce()
    {
        var source = new TestSource();
        var sound = new OutgoingSound(new TestCapture(), source);

        sound.Dispose();
        sound.Dispose();

        Assert.Equal(1, source.DisposeCount);
    }

    private sealed class TestCapture : IAudioCapture
    {
        public bool IsStarted { get; private set; }
        public bool IsDisposed { get; private set; }

        public event AudioSamplesHandler? SamplesReady;

        public void Emit(ReadOnlySpan<short> samples) => SamplesReady?.Invoke(samples);

        public bool Start() => IsStarted = true;

        public void Dispose() => IsDisposed = true;
    }

    private sealed class TestSource : IAudioSendSource
    {
        public int DisposeCount { get; private set; }
        public List<short[]> Frames { get; } = [];

        public void Push(ReadOnlySpan<short> samples, int sampleRate, int channels)
        {
            Assert.Equal(IAudioCapture.SampleRate, sampleRate);
            Assert.Equal(IAudioCapture.Channels, channels);
            Frames.Add(samples.ToArray());
        }

        public void Dispose() => DisposeCount++;
    }
}
