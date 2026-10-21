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

    public static string Get(RequestRejectReason reason) => reason switch
    {
        RequestRejectReason.RateLimited => Strings.Get("rejection.rateLimited"),
        RequestRejectReason.InvalidRequest => Strings.Get("rejection.invalidRequest"),
        RequestRejectReason.InsufficientPermissions => Strings.Get("rejection.insufficientPermissions"),
        RequestRejectReason.UserFrozen => Strings.Get("rejection.userFrozen"),
        _ => Strings.Get("rejection.refused")
    };

    public static string GetForConnection(RequestRejectReason reason) => reason switch
    {
        RequestRejectReason.RateLimited => Strings.Get("connect.failed.rateLimited"),
        RequestRejectReason.InvalidRequest => Strings.Get("connect.failed.invalidRequest"),
        _ => Strings.Get("connect.failed.refused")
    };
}
