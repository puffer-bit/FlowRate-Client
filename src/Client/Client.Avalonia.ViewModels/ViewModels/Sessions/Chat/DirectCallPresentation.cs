using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.Sessions.Chat;

public static class DirectCallPresentation
{
    public static async Task<string> GetPeerNameAsync(ICoordinatorSession coordinator, DirectCallDTO call)
    {
        var peerId = coordinator.GetCurrentUser().Id == call.CallerId ? call.CalleeId : call.CallerId;
        var peer = await coordinator.GetUser(peerId);
        if (!string.IsNullOrWhiteSpace(peer?.DisplayName)) return peer.DisplayName;
        if (!string.IsNullOrWhiteSpace(peer?.Username)) return peer.Username;
        return Strings.Get("calls.peer");
    }
}
