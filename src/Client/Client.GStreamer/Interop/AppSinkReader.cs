using System.Diagnostics;
using Gst;

namespace Client.GStreamer.Interop;

/// <summary>
/// Takes the samples out of an appsink on a thread of its own and hands them to a callback.
/// </summary>
/// <remarks>
/// The sink is polled rather than watched through its new-sample signal, which would run managed
/// code on a streaming thread.
/// </remarks>
internal sealed class AppSinkReader : IDisposable
{
    // Also how long disposing may wait for the thread to notice
    private const ulong PullTimeoutNanoseconds = 100_000_000;
    private static readonly TimeSpan IdlePause = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan FailurePause = TimeSpan.FromMilliseconds(100);

    private readonly Element _sink;
    private readonly Action<Sample> _onSample;
    private readonly Action<Exception> _onError;
    private readonly Thread _thread;
    private volatile bool _isStopped;

    /// <param name="sink">
    /// An appsink parsed from a description, already inside its bin. The reader takes over the
    /// wrapper and disposes it when it stops.
    /// </param>
    /// <param name="name">Names the reader thread.</param>
    /// <param name="onSample">
    /// Runs on the reader thread. The sample is disposed after it returns, so the receiver copies
    /// what it keeps.
    /// </param>
    /// <param name="onError">Receives what <paramref name="onSample"/> throws, on the reader thread.</param>
    public AppSinkReader(Element sink, string name, Action<Sample> onSample, Action<Exception> onError)
    {
        _sink = sink;
        _onSample = onSample;
        _onError = onError;
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    private void Run()
    {
        while (!_isStopped)
        {
            try
            {
                var started = Stopwatch.GetTimestamp();

                // Disposed here rather than left to the GC: a sample holds a buffer of the pool it
                // came from, and a source with a fixed pool stops producing once they are all held
                using var sample = GstApp.TryPullSample(_sink, PullTimeoutNanoseconds);

                if (sample == null)
                {
                    // A sink that is not running yet, or not any more, answers at once instead of waiting
                    if (Stopwatch.GetElapsedTime(started) < IdlePause)
                        Thread.Sleep(IdlePause);

                    continue;
                }

                _onSample(sample);
            }
            catch (Exception exception)
            {
                if (_isStopped)
                    return;

                _onError(exception);

                // A sink that keeps failing must not spin the thread
                Thread.Sleep(FailurePause);
            }
        }
    }

    /// <summary>
    /// Stops the thread and waits for a sample that is being handled, unless called from the
    /// callback itself. Call before the sink is shut down.
    /// </summary>
    public void Dispose()
    {
        if (_isStopped)
            return;

        _isStopped = true;

        if (Thread.CurrentThread == _thread)
            return;

        _thread.Join();

        // Not left to the GC: a collected wrapper lets its element go only when the release queue of
        // the binding is drained
        _sink.Dispose();
    }
}
