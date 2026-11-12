using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Responses.SessionActions.ChatSession;
namespace Client.Api;
internal partial class CoordinatorGateway
{
    public async Task<DirectCallDTO> ChangeDirectCall(int chatId, Guid callId, DirectCallAction action) =>
        (await SendRequestAsync<DirectCallResponse>(new DirectCallRequest(chatId, callId, action))).Call;
}
