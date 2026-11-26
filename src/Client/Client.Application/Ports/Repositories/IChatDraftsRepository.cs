namespace Client.Application.Ports.Repositories;

/// <summary>The texts typed in chats and not sent, keyed by the coordinator instance and the chat.</summary>
public interface IChatDraftsRepository
{
    /// <summary>Every draft of the coordinator, by its chat.</summary>
    Task<IReadOnlyDictionary<int, string>> GetAllAsync(string coordinatorInstanceId);

    /// <summary>Replaces a draft. An empty or whitespace-only draft removes the stored entry.</summary>
    Task SetAsync(string coordinatorInstanceId, int chatId, string? text);
}
