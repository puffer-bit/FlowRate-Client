using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Microsoft.Extensions.Logging;

namespace Client.Application.Chat;

internal sealed partial class ChatCache : IChatCache
{
    private readonly IChatCacheRepository _repository;
    private readonly ILogger _logger;
    private readonly ChatCacheKey _chat;
    private readonly int _currentUserId;

    // The last queued call; the next one starts when it ends
    private readonly Lock _queueLock = new();
    private Task _queue = Task.CompletedTask;

    private readonly Func<bool> _isEnabled;

    public ChatCache(IChatCacheRepository repository, ILogger logger, ChatCacheKey chat, int currentUserId,
        Func<bool> isEnabled)
    {
        _isEnabled = isEnabled;
        _repository = repository;
        _logger = logger;
        _chat = chat;
        _currentUserId = currentUserId;
    }

    public Task<CachedChatPage?> LoadNewestAsync(int take) =>
        Enqueue(async () => MarkMine(await _repository.GetNewestAsync(_chat, take)));

    public Task<CachedChatPage?> LoadBeforeAsync(int beforeMessageId, int take) =>
        Enqueue(async () => MarkMine(await _repository.GetBeforeAsync(_chat, beforeMessageId, take)));

    public void Replace(IReadOnlyList<ChatMessageModel> messages, long revision, bool reachesStart) =>
        Enqueue(() => _repository.ReplaceAsync(_chat, messages, revision, reachesStart));

    public void AddOlder(int beforeMessageId, IReadOnlyList<ChatMessageModel> messages, bool reachesStart) =>
        Enqueue(() => _repository.AddOlderAsync(_chat, beforeMessageId, messages, reachesStart));

    public void Apply(IReadOnlyList<ChatMessageModel> changes, long revision)
    {
        var changed = changes.Where(message => !message.IsDeleted).ToList();
        var deletedIds = changes.Where(message => message.IsDeleted).Select(message => message.Id).ToList();

        Enqueue(() => _repository.ApplyAsync(_chat, changed, deletedIds, revision));
    }

    public void Remove(int messageId, long revision) =>
        Enqueue(() => _repository.ApplyAsync(_chat, [], [messageId], revision));

    private CachedChatPage? MarkMine(CachedChatPage? page)
    {
        if (page == null)
            return null;

        foreach (var message in page.Messages)
            message.IsMine = message.SenderUserId == _currentUserId;

        return page;
    }

    private void Enqueue(Func<Task> operation) =>
        Enqueue<object>(async () =>
        {
            await operation();
            return null;
        });

    private Task<T?> Enqueue<T>(Func<Task<T?>> operation) where T : class
    {
        lock (_queueLock)
        {
            var task = RunAfterAsync(_queue, operation);
            _queue = task;
            return task;
        }
    }

    // Never fails, so one failed call does not stop the ones queued after it
    private async Task<T?> RunAfterAsync<T>(Task previous, Func<Task<T?>> operation) where T : class
    {
        await previous.ConfigureAwait(false);

        // Asked when the call runs, not when it was made: the user may have turned the keeping off since
        if (!_isEnabled())
            return null;

        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogFailed(exception, _chat.ChatId);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Warning, "The cache of chat {ChatId} failed, going on without it")]
    private partial void LogFailed(Exception exception, int chatId);
}
