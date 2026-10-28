using Client.Application.Ports;
using Client.LibWebRTC.Voice;
using Xunit;

namespace Client.Application.Tests;

public class OutgoingSoundTests
{
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

        public event AudioSamplesHandler? SamplesReady
        {
            add { }
            remove { }
        }

        public bool Start() => IsStarted = true;

        public void Dispose() => IsDisposed = true;
    }

    private sealed class TestSource : IAudioSendSource
    {
        public int DisposeCount { get; private set; }

        public void Push(ReadOnlySpan<short> samples, int sampleRate, int channels)
        {
        }

        public void Dispose() => DisposeCount++;
    }
}
