using Serilog.Events;

namespace Client.Tools.Logging;

/// <summary>
/// One event of the journal as <see cref="InMemoryLog"/> keeps it: already rendered, so a view shows it
/// without knowing Serilog.
/// </summary>
/// <param name="Sequence">Grows by one with every event, so that a view can tell what it has not shown yet.</param>
/// <param name="Category">The full category, e.g. <c>Client.Api.Transport.WebSocketGateway</c>.</param>
/// <param name="ShortCategory">Its last segment, <c>WebSocketGateway</c>.</param>
/// <param name="Exception">The exception with its stack, or null.</param>
public sealed record LogEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string? Category,
    string? ShortCategory,
    string Message,
    string? Exception);
