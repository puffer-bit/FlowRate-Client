using Client.Application.Ports;
using Client.Core.Coordinator;
using Shared.Models.DataTransferObjects;
namespace Client.Application.Coordinator;
public partial class CoordinatorSession
{
    public event Action<DirectCallDTO>? DirectCallChanged;
    public event Action<Shared.Enums.ErrorCode>? DirectCallFailed;
    public DirectCallDTO? DirectCall { get; private set; }
    private readonly Lock _callLock = new();

    public async Task<RequestResult<DirectCallDTO>> ChangeDirectCall(int chatId, Guid callId, DirectCallAction action)
    {
        var result = await SendAsync(() => _gateway.ChangeDirectCall(chatId, callId, action));
        if (result.IsSuccess && result.Value is { } call) ApplyDirectCall(call);
        else if (!result.IsRejected && result.Error is { } error) DirectCallFailed?.Invoke(error);
        return result;
    }

    private void ApplyDirectCall(DirectCallDTO call)
    {
        lock (_callLock)
        {
            if (ConnectionStatus != CoordinatorState.Connected) return;
            // Notifications can arrive before their corresponding request completes.
            if (DirectCall is { } previous && previous.CallId == call.CallId && previous.State >= call.State) return;
            if (DirectCall is { } newer && newer.CallId != call.CallId && newer.CreatedAt > call.CreatedAt) return;
            DirectCall = call;
        }
        DirectCallChanged?.Invoke(call);
    }
}
