using Avalonia;
using Avalonia.Logging;
using Serilog;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using SerilogLevel = Serilog.Events.LogEventLevel;

namespace Client.Services.Logging;

/// <summary>
/// Hands the messages of Avalonia to the journal of the application, under the category
/// <c>Avalonia.{Area}</c>; with <c>LogToTrace</c> they went to the trace listeners, which nobody reads.
/// </summary>
public sealed class AvaloniaLogSink(AvaloniaLevel minimumLevel) : ILogSink
{
    public bool IsEnabled(AvaloniaLevel level, string area) => level >= minimumLevel;

    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate,
        params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
            return;

        // The level enums of Avalonia and Serilog have the same members in the same order
        var journalLevel = (SerilogLevel)level;

        // A binding to a view model that is not there yet, e.g. the panel of a session before one is
        // opened, warns on every start; it is worth reading only while looking for a binding mistake
        if (area == LogArea.Binding && level == AvaloniaLevel.Warning)
            journalLevel = SerilogLevel.Debug;

        Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, $"Avalonia.{area}")
            .ForContext("AvaloniaSource", source?.GetType().Name)
            .Write(journalLevel, messageTemplate, propertyValues);
    }
}

public static class AvaloniaLogSinkExtensions
{
    /// <summary>
    /// Sends the messages of Avalonia from <paramref name="minimumLevel"/> up to the journal, in place of
    /// <c>LogToTrace</c>.
    /// </summary>
    public static AppBuilder LogToJournal(this AppBuilder builder, AvaloniaLevel minimumLevel = AvaloniaLevel.Warning)
    {
        Logger.Sink = new AvaloniaLogSink(minimumLevel);
        return builder;
    }
}
