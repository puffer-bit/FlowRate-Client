namespace Client.Api;

/// <summary>
/// A request could not complete because the connection to the coordinator is gone. Derived from
/// <see cref="TimeoutException"/>, so callers treat it the same as a request that got no answer.
/// </summary>
public sealed class ConnectionLostException(string message) : TimeoutException(message);
