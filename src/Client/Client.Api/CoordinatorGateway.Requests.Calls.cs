using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Responses.SessionActions.ChatSession;
namespace Client.Api;
internal partial class CoordinatorGateway
{
    public async Task<ChatCallDTO> ChangeChatCall(int chatId, Guid callId, ChatCallAction action, int? targetUserId) =>
        (await SendRequestAsync<ChatCallResponse>(new ChatCallRequest(chatId, callId, action, targetUserId))).Call;
}
