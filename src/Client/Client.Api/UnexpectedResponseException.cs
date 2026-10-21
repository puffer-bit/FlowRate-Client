namespace Client.Api;

/// <summary>
/// The coordinator answered a request with a response of another type: the client and the server
/// speak different versions of the protocol.
/// </summary>
public sealed class UnexpectedResponseException(string message) : Exception(message);
