using System.Diagnostics.CodeAnalysis;
using Shared.Enums;

namespace Client.Core.Coordinator;

/// <summary>
/// How a request to the coordinator ended: <see cref="Error"/> is null when it was done. A request that got
/// no answer in time, or lost its connection, fails with <see cref="ErrorCode.TimedOut"/>, like one the server
/// did not get done in time; one not sent for breaking the shared validation limits, with
/// <see cref="ErrorCode.InvalidRequest"/>, and one the client checks itself first, such as a name, with
/// <see cref="ErrorCode.InvalidValue"/>.
/// </summary>
/// <param name="Fields">
/// Why values were refused, by field ("Heartbeat.TimeoutSeconds"; "" for the request as a whole);
/// with <see cref="ErrorCode.InvalidRequest"/> and <see cref="ErrorCode.InvalidValue"/>.
/// </param>
public readonly record struct RequestResult(ErrorCode? Error, IReadOnlyDictionary<string, string[]>? Fields = null)
{
    public static RequestResult Success => default;

    public bool IsSuccess => Error is null;

    /// <summary>
    /// The request was refused as a whole: too many requests, data outside the shared validation limits, missing
    /// permissions or a frozen user. The user is told about these apart from the request that failed.
    /// </summary>
    public bool IsRejected => Error is ErrorCode.RateLimited or ErrorCode.InvalidRequest
        or ErrorCode.InsufficientPermissions or ErrorCode.UserFrozen;

    public override string ToString() => Error?.ToString() ?? "Success";

    public static implicit operator RequestResult(ErrorCode error) => new(error);
}

/// <summary>
/// The same for a request that returns a value, which is there on success only.
/// </summary>
public readonly record struct RequestResult<T>
{
    public RequestResult(T value) => Value = value;

    public RequestResult(ErrorCode error, IReadOnlyDictionary<string, string[]>? fields = null)
    {
        Error = error;
        Fields = fields;
    }

    public T? Value { get; }

    public ErrorCode? Error { get; }

    /// <inheritdoc cref="RequestResult.Fields"/>
    public IReadOnlyDictionary<string, string[]>? Fields { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess => Error is null;

    /// <inheritdoc cref="RequestResult.IsRejected"/>
    public bool IsRejected => Result.IsRejected;

    /// <summary>The outcome without the value.</summary>
    public RequestResult Result => new(Error, Fields);

    public void Deconstruct(out RequestResult result, out T? value)
    {
        result = Result;
        value = Value;
    }

    public override string ToString() => Error?.ToString() ?? "Success";

    public static implicit operator RequestResult<T>(T value) => new(value);

    public static implicit operator RequestResult<T>(ErrorCode error) => new(error);

    public static implicit operator RequestResult<T>(RequestResult result) =>
        result.Error is { } error
            ? new RequestResult<T>(error, result.Fields)
            : throw new InvalidOperationException("A successful request needs its value");
}
