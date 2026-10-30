using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Client.Tools.Logging;

/// <summary>
/// The last events of the journal, kept in memory for a view inside the application. Holds at most
/// <see cref="Capacity"/> of them: the oldest go first. Everything that passes the level of the journal
/// comes here, the native logs of GStreamer and libwebrtc too.
/// </summary>
public sealed class InMemoryLog : ILogEventSink
{
    // {Message:l} writes strings as they are; RenderMessage would put them in quotes
    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:l}");

    private readonly Lock _lock = new();
    private readonly LogEntry?[] _entries;
    private long _sequence;
    private int _next;
    private int _count;

    public InMemoryLog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _entries = new LogEntry?[capacity];
    }

    public int Capacity => _entries.Length;

    /// <summary>
    /// Raised on the thread that wrote the event, often a streaming thread of GStreamer or libwebrtc:
    /// the handler must be quick and must not touch the interface. A view is better off collecting the
    /// entries and showing them in batches.
    /// </summary>
    public event Action<LogEntry>? EntryAdded;

    /// <summary>
    /// Raised when <see cref="Clear"/> empties the journal.
    /// </summary>
    public event Action? Cleared;

    /// <summary>
    /// The kept entries, the oldest first.
    /// </summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            var result = new LogEntry[_count];
            var start = (_next - _count + _entries.Length) % _entries.Length;

            for (var i = 0; i < _count; i++)
                result[i] = _entries[(start + i) % _entries.Length]!;

            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_entries);
            _next = 0;
            _count = 0;
        }

        Cleared?.Invoke();
    }

    public void Emit(LogEvent logEvent)
    {
        var message = new StringWriter();
        MessageFormatter.Format(logEvent, message);

        var category = logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
                       && value is ScalarValue { Value: string sourceContext }
            ? sourceContext
            : null;
        var lastDot = category?.LastIndexOf('.') ?? -1;

        LogEntry entry;

        lock (_lock)
        {
            entry = new LogEntry(++_sequence, logEvent.Timestamp, logEvent.Level, category,
                lastDot < 0 ? category : category![(lastDot + 1)..], message.ToString(),
                logEvent.Exception?.ToString());

            _entries[_next] = entry;
            _next = (_next + 1) % _entries.Length;
            _count = Math.Min(_count + 1, _entries.Length);
        }

        // Outside the lock: a slow handler must not hold up the other writers
        EntryAdded?.Invoke(entry);
    }
}
