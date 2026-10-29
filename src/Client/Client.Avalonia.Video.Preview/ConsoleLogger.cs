using System;
using Microsoft.Extensions.Logging;

namespace Client.Avalonia.Video.Preview;

/// <summary>Prints what the video surface reports, so a failed fast path shows in the terminal.</summary>
internal sealed class ConsoleLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");

        if (exception != null)
            Console.WriteLine(exception);
    }
}
