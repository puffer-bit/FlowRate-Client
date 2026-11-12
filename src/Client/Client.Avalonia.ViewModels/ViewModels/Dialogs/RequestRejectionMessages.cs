using Client.Avalonia.Localization;
using Shared.Enums;

namespace Client.ViewModels.Dialogs;

/// <summary>
/// What the user is told about a refused request. The details of a rejection are written for the
/// logs and never shown.
/// </summary>
public static class RequestRejectionMessages
{
    public static string Title => Strings.Get("rejection.title");

    public static string Get(ErrorCode code) => code switch
    {
        ErrorCode.RateLimited => Strings.Get("rejection.rateLimited"),
        ErrorCode.InvalidRequest => Strings.Get("rejection.invalidRequest"),
        ErrorCode.InsufficientPermissions => Strings.Get("rejection.insufficientPermissions"),
        ErrorCode.UserFrozen => Strings.Get("rejection.userFrozen"),
        _ => Strings.Get("rejection.refused")
    };

    public static string GetForConnection(ErrorCode code) => code switch
    {
        ErrorCode.RateLimited => Strings.Get("connect.failed.rateLimited"),
        ErrorCode.InvalidRequest => Strings.Get("connect.failed.invalidRequest"),
        _ => Strings.Get("connect.failed.refused")
    };
}
