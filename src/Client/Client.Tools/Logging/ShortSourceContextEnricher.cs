using Serilog.Core;
using Serilog.Events;

namespace Client.Tools.Logging;

/// <summary>
/// Adds <c>ShortContext</c>, the last segment of the category, so that the console shows
/// <c>WebSocketGateway</c> instead of <c>Client.Api.Transport.WebSocketGateway</c>.
/// </summary>
internal sealed class ShortSourceContextEnricher : ILogEventEnricher
{
    public const string PropertyName = "ShortContext";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
            || value is not ScalarValue { Value: string sourceContext })
        {
            return;
        }

        var lastDot = sourceContext.LastIndexOf('.');
        var shortContext = lastDot < 0 ? sourceContext : sourceContext[(lastDot + 1)..];

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(PropertyName, shortContext));
    }
}
