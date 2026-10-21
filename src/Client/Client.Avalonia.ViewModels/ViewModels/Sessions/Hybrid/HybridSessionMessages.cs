using Client.Application.Sessions.Hybrid;
using Client.Avalonia.Localization;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// What the user is told about an error of a voice session.
/// </summary>
public static class HybridSessionMessages
{
    public static string Get(HybridSessionError error)
    {
        var text = Strings.Get(error.Kind switch
        {
            HybridSessionErrorKind.TransportStartFailed => "hybrid.error.transportStartFailed",
            HybridSessionErrorKind.DtlsParametersRejected => "hybrid.error.dtlsRejected",
            HybridSessionErrorKind.DtlsParametersSendFailed => "hybrid.error.dtlsSendFailed",
            _ => "hybrid.error.rejoinFailed"
        });

        return error.Details == null ? text : $"{text}: {error.Details}";
    }
}
