using Shared.Enums;

namespace Client.Application.Ports;

/// <summary>
/// The request failed: the server answered it with an error, or it was refused already here, when it breaks the
/// shared validation limits (<see cref="ErrorCode.InvalidRequest"/>). Derived from <see cref="TimeoutException"/>,
/// so callers that do not handle it treat it as a request that got no answer.
/// </summary>
/// <param name="fields">Why values were refused, by field; see <see cref="Shared.Models.Responses.ErrorResponse.Fields"/>.</param>
public sealed class RequestFailedException(ErrorCode code, string? details,
    IReadOnlyDictionary<string, string[]>? fields = null)
    : TimeoutException($"Request failed: {code}{(details == null ? "" : $" ({details})")}")
{
    public ErrorCode Code { get; } = code;
    public string? Details { get; } = details;
    public IReadOnlyDictionary<string, string[]>? Fields { get; } = fields;
}
