using Microsoft.Extensions.Logging;
using Shared.Enums;

namespace Client.Api.Protocol;

internal partial class ResponseAwaiter
{
    [LoggerMessage(LogLevel.Debug, "Request {RequestId} failed: {Code} {Details}")]
    private partial void LogRequestFailed(Guid requestId, ErrorCode code, string? details);

    [LoggerMessage(LogLevel.Trace, "Response to an unknown request {RequestId} dropped")]
    private partial void LogUnknownResponseDropped(Guid requestId);
}
