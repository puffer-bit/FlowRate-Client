using System;
using System.Diagnostics;

namespace Client.Avalonia.Controls.Chat;

/// <summary>
/// Times what opening a chat costs, for the log: from the choice of the chat to its first frame with
/// messages, with the layout passes of the message list in between and the garbage collections the
/// opening caused. One opening is traced at a time, on the UI thread; the trace ends by itself after a
/// few seconds, so a chat that never gets its messages does not leave it running.
/// </summary>
public static class ChatOpenTrace
{
    // A trace that has not ended by then is closed, and what comes after it is not logged
    private const double MaxTraceMilliseconds = 5000;

    private static long _start;
    private static bool _isActive;
    private static int _gen0;
    private static int _gen1;
    private static int _gen2;
    private static string _chat = "";

    /// <summary>Where the lines go; the host sets it to its log. Without it nothing is traced.</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>Whether an opening is being traced now.</summary>
    public static bool IsActive
    {
        get
        {
            if (_isActive && Elapsed > MaxTraceMilliseconds)
                End("timed out");
            return _isActive;
        }
    }

    private static double Elapsed => Stopwatch.GetElapsedTime(_start).TotalMilliseconds;

    /// <summary>Starts the trace of a chat being opened, ending the one before.</summary>
    public static void Begin(string chat)
    {
        if (Sink == null)
            return;

        if (_isActive)
            End("replaced");

        _chat = chat;
        _start = Stopwatch.GetTimestamp();
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _isActive = true;
        Sink($"Chat open {_chat}: started");
    }

    /// <summary>Notes a step, with the time since the start.</summary>
    public static void Mark(string step)
    {
        if (IsActive)
            Sink?.Invoke($"Chat open {_chat}: +{Elapsed:F1} ms {step}");
    }

    /// <summary>Times what runs until the returned value is disposed.</summary>
    public static Scope Measure(string step) => new(IsActive ? step : null);

    /// <summary>Ends the trace with the collections it saw.</summary>
    public static void End(string step)
    {
        if (!_isActive)
            return;

        _isActive = false;
        Sink?.Invoke($"Chat open {_chat}: +{Elapsed:F1} ms {step}; GC gen0 {GC.CollectionCount(0) - _gen0}, " +
            $"gen1 {GC.CollectionCount(1) - _gen1}, gen2 {GC.CollectionCount(2) - _gen2}");
    }

    public readonly struct Scope : IDisposable
    {
        private readonly string? _step;
        private readonly long _start;

        internal Scope(string? step)
        {
            _step = step;
            _start = step == null ? 0 : Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_step != null)
                Mark($"{_step} took {Stopwatch.GetElapsedTime(_start).TotalMilliseconds:F1} ms");
        }
    }
}
