using Client.Tools.Logging;
using Serilog;
using Serilog.Events;
using Xunit;

namespace Client.Application.Tests;

public class InMemoryLogTests
{
    private static (InMemoryLog Memory, Serilog.Core.Logger Logger) Create(int capacity)
    {
        var memory = new InMemoryLog(capacity);
        var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(memory).CreateLogger();
        return (memory, logger);
    }

    [Fact]
    public void KeepsTheNewestEntriesOldestFirst()
    {
        var (memory, logger) = Create(capacity: 3);

        for (var i = 1; i <= 5; i++)
            logger.Information("Message {Number}", i);

        var entries = memory.Snapshot();

        Assert.Equal(["Message 3", "Message 4", "Message 5"], entries.Select(entry => entry.Message));
        Assert.Equal([3L, 4L, 5L], entries.Select(entry => entry.Sequence));
    }

    [Fact]
    public void RendersStringsWithoutQuotesAndSplitsTheCategory()
    {
        var (memory, logger) = Create(capacity: 10);

        logger.ForContext("SourceContext", "Client.Api.Transport.WebSocketGateway")
            .Warning(new InvalidOperationException("boom"), "Dropped {Type}", "Ping");

        var entry = Assert.Single(memory.Snapshot());

        Assert.Equal("Dropped Ping", entry.Message);
        Assert.Equal(LogEventLevel.Warning, entry.Level);
        Assert.Equal("Client.Api.Transport.WebSocketGateway", entry.Category);
        Assert.Equal("WebSocketGateway", entry.ShortCategory);
        Assert.Contains("boom", entry.Exception);
    }

    [Fact]
    public void ReportsNewEntriesAndClearing()
    {
        var (memory, logger) = Create(capacity: 10);
        var added = new List<LogEntry>();
        var isCleared = false;
        memory.EntryAdded += added.Add;
        memory.Cleared += () => isCleared = true;

        logger.Debug("First");
        memory.Clear();
        logger.Debug("Second");

        Assert.Equal(["First", "Second"], added.Select(entry => entry.Message));
        Assert.True(isCleared);
        // The sequence goes on after clearing, so a view does not take new entries for old ones
        Assert.Equal(2, Assert.Single(memory.Snapshot()).Sequence);
    }
}
