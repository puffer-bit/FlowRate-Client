using Client.Application.Ports;
using Client.Core.Coordinator;
using Shared.Models.DataTransferObjects;
namespace Client.Application.Coordinator;
public partial class CoordinatorSession
{
    public event Action<ChatCallDTO>? ChatCallChanged;
    public event Action<Shared.Enums.ErrorCode>? ChatCallFailed;
    private readonly Lock _callLock = new();
    private readonly Dictionary<int, ChatCallDTO> _chatCalls = new();
    // Calls heard of through notifications, which the server sends in order; ended ones stay ended
    private readonly HashSet<Guid> _announcedCalls = new();
    private readonly HashSet<Guid> _endedCalls = new();

    public IReadOnlyDictionary<int, ChatCallDTO> ChatCalls
    {
        get { lock (_callLock) return new Dictionary<int, ChatCallDTO>(_chatCalls); }
    }

    public ChatCallDTO? CurrentCall
    {
        get { lock (_callLock) return _chatCalls.Values.FirstOrDefault(c => c.IsSelectedDevice); }
    }

    public async Task<RequestResult<ChatCallDTO>> ChangeChatCall(int chatId, Guid callId, ChatCallAction action,
        int? targetUserId = null)
    {
        var result = await SendAsync(() => _gateway.ChangeChatCall(chatId, callId, action, targetUserId));
        if (result.IsSuccess && result.Value is { } call) ApplyChatCall(call, false);
        else if (!result.IsRejected && result.Error is { } error) ChatCallFailed?.Invoke(error);
        return result;
    }

    /// <param name="isNotification">
    /// Notifications come in the order of the changes. A response can be overtaken by a later change, so it only
    /// counts for a call no notification has told about yet.
    /// </param>
    private void ApplyChatCall(ChatCallDTO call, bool isNotification)
    {
        lock (_callLock)
        {
            if (ConnectionStatus != CoordinatorState.Connected) return;
            if (_endedCalls.Contains(call.CallId)) return;
            if (!isNotification && _announcedCalls.Contains(call.CallId)) return;
            if (isNotification) _announcedCalls.Add(call.CallId);
            if (call.State == ChatCallState.Ended)
            {
                _endedCalls.Add(call.CallId);
                if (_chatCalls.TryGetValue(call.ChatId, out var known) && known.CallId == call.CallId)
                    _chatCalls.Remove(call.ChatId);
            }
            else _chatCalls[call.ChatId] = call;
        }
        ChatCallChanged?.Invoke(call);
    }
}
