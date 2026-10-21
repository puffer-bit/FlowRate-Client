namespace Client.Application.Sessions.Hybrid;

public enum HybridSessionErrorKind
{
    TransportStartFailed,
    DtlsParametersRejected,
    DtlsParametersSendFailed,
    RejoinFailed
}

/// <summary>
/// What went wrong in a voice session. The interface words <see cref="Kind"/> for the user.
/// </summary>
/// <param name="Details">A result code or the message of an exception, as a technical detail.</param>
public sealed record HybridSessionError(HybridSessionErrorKind Kind, string? Details = null);
