using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Sessions;
using Shared.Enums;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.Sessions.Chat;

public static class CallPresentation
{
    /// <summary>
    /// What a ring shows: the group name, or in a direct chat the other member; and who started the call.
    /// </summary>
    public static async Task<(string ChatName, string CallerName)> GetNamesAsync(ICoordinatorSession coordinator,
        ChatCallDTO call, ChatSessionModel? chat)
    {
        var caller = await GetUserNameAsync(coordinator, call.CallerId);
        if (chat is { Kind: ChatKind.Group } group)
            return (group.Name, caller);
        var me = coordinator.GetCurrentUser().Id;
        if (call.CallerId != me)
            return (caller, caller);
        var peerId = chat?.MemberIds is { } members ? System.Array.Find(members, id => id != me) : 0;
        return (peerId > 0 ? await GetUserNameAsync(coordinator, peerId) : Strings.Get("calls.peer"), caller);
    }

    private static async Task<string> GetUserNameAsync(ICoordinatorSession coordinator, int userId)
    {
        var user = await coordinator.GetUser(userId);
        if (!string.IsNullOrWhiteSpace(user?.DisplayName)) return user.DisplayName;
        if (!string.IsNullOrWhiteSpace(user?.Username)) return user.Username;
        return Strings.Get("calls.peer");
    }
}
