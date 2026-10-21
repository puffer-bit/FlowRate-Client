using Microsoft.Extensions.Logging;
using Shared.Enums;

namespace Client.Api.Protocol;

internal partial class ResponseAwaiter
{
    [LoggerMessage(LogLevel.Warning, "Request {RequestId} rejected: {Reason} {Details}")]
    private partial void LogRequestRejected(Guid requestId, RequestRejectReason reason, string? details);

    [LoggerMessage(LogLevel.Trace, "Response to an unknown request {RequestId} dropped")]
    private partial void LogUnknownResponseDropped(Guid requestId);
}
