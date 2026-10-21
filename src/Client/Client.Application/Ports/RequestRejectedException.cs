using Shared.Enums;

namespace Client.Application.Ports;

/// <summary>
/// The request was refused before it was handled: by the server, e.g. for exceeding a rate limit,
/// or already here, when it breaks the shared validation limits. Derived from
/// <see cref="TimeoutException"/>, so callers that do not handle it treat it as a request that got no answer.
/// </summary>
public sealed class RequestRejectedException(RequestRejectReason reason, string? details)
    : TimeoutException($"Request rejected: {reason}{(details == null ? "" : $" ({details})")}")
{
    public RequestRejectReason Reason { get; } = reason;
    public string? Details { get; } = details;
}
