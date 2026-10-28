using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Interop;

/// <summary>
/// Takes the messages of a bus on a managed thread of its own, for code that runs no GLib main loop.
/// </summary>
/// <remarks>
/// Use this instead of <c>SyncMessage</c> handlers. Those run on the thread that posts the message,
/// which for PulseAudio elements and the PulseAudio device provider is a libpulse thread, and libpulse
/// blocks every signal in its threads. Managed code there cannot survive a signal: under a debugger
/// the breakpoint the runtime sets on a new thread's first managed instruction kills the process with
/// SIGTRAP, and without one a NullReferenceException (a SIGSEGV underneath) does the same.
/// </remarks>
public sealed partial class GstBusWatcher : IDisposable
{
    // Also how long disposing may wait for the thread to notice
    private static readonly ClockTime PollTimeout = ClockTime.FromNanoseconds(100_000_000);

    private readonly Bus _bus;
    private readonly MessageType _types;
    private readonly Action<Message> _handler;
    private readonly ILogger _logger;
    private readonly Thread _thread;
    private volatile bool _isDisposed;

    /// <param name="types">Messages of other types are taken off the bus and dropped.</param>
    /// <param name="handler">Runs on the watcher thread; the message is disposed after it returns.</param>
    /// <param name="name">Names the watcher thread.</param>
    /// <param name="logger">The owner's logger, so that failures show up under the owner.</param>
    public GstBusWatcher(Bus bus, MessageType types, Action<Message> handler, string name, ILogger logger)
    {
        _bus = bus;
        _types = types;
        _handler = handler;
        _logger = logger;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = $"{name} bus"
        };
        _thread.Start();
    }

    private void Run()
    {
        while (!_isDisposed)
        {
            var message = _bus.TimedPopFiltered(PollTimeout, _types);

            // The binding queues the releases of collected wrappers for a GLib main loop, and the
            // application runs none; a watcher is running whenever GStreamer is in use
            GstSharp.DrainPendingReleases();

            if (message == null)
                continue;

            try
            {
                if (!_isDisposed)
                    _handler(message);
            }
            catch (Exception exception)
            {
                LogHandlerFailed(exception, message.Type);
            }
            finally
            {
                message.Dispose();
            }
        }
    }

    /// <summary>
    /// Stops the thread and waits for it, unless called from the handler itself.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        if (Thread.CurrentThread == _thread)
            return;

        _thread.Join();

        // The watcher owns the wrapper it was given; a dropped one would hold the bus for good
        _bus.Dispose();
    }
}
