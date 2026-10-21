using Client.Application.Ports.Repositories;
using Microsoft.Extensions.Logging;

namespace Client.Application.Chat;

internal sealed partial class ChatCacheStore : IChatCacheStore
{
    private readonly IChatCacheRepository _repository;
    private readonly ILogger<ChatCacheStore> _logger;

    public ChatCacheStore(IChatCacheRepository repository, ILogger<ChatCacheStore> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public bool IsEnabled { get; set; } = true;

    public IChatCache Open(string coordinatorInstanceId, int chatId, int currentUserId) =>
        new ChatCache(_repository, _logger, new ChatCacheKey(coordinatorInstanceId, chatId), currentUserId,
            () => IsEnabled);

    public void Remove(string coordinatorInstanceId, int chatId) =>
        _ = RunAsync(() => _repository.RemoveAsync(new ChatCacheKey(coordinatorInstanceId, chatId)));

    public void RemoveExcept(string coordinatorInstanceId, IReadOnlyCollection<int> existingChatIds) =>
        _ = RunAsync(() => _repository.RemoveExceptAsync(coordinatorInstanceId, existingChatIds));

    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogCleanupFailed(exception);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Removing cached chats failed")]
    private partial void LogCleanupFailed(Exception exception);
}
