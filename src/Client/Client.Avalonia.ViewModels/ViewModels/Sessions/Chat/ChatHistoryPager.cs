using System.Threading.Tasks;
using Shared.Enums;
using Client.Core.Chat;
using Client.Application.Chat;
using Client.Application.Coordinator;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Pages a chat's history from newest to oldest. A page is asked for by the id of the oldest
/// message already held, so messages that arrive or disappear meanwhile cannot shift it.
/// Pages come from the local cache as far as it goes and from the server below that; what the
/// server sends is put into the cache.
/// </summary>
public class ChatHistoryPager
{
    public const int PageSize = 30;

    public bool IsFullyLoaded { get; private set; }
    public bool IsBusy { get; private set; }

    private readonly ICoordinatorSession _coordinatorSession;
    private readonly IChatCache _cache;
    private readonly int _chatId;

    public ChatHistoryPager(ICoordinatorSession coordinatorSession, IChatCache cache, int chatId)
    {
        _coordinatorSession = coordinatorSession;
        _cache = cache;
        _chatId = chatId;
    }

    /// <summary>
    /// The newest page of the cache, current at its revision and not at the one of the chat.
    /// Returns null when the chat is not cached or a request is already running.
    /// </summary>
    public async Task<ChatHistoryPage?> LoadCachedAsync()
    {
        if (IsBusy)
            return null;

        IsBusy = true;
        try
        {
            var cached = await _cache.LoadNewestAsync(PageSize);
            if (cached == null)
                return null;

            IsFullyLoaded = cached.ReachesStart;
            return new ChatHistoryPage(cached.Messages, cached.Revision);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Loads the page that precedes <paramref name="beforeMessageId"/>, or the newest one from the server when it is null.
    /// Returns null when history is exhausted, a request is already running, or the request failed.
    /// </summary>
    public async Task<ChatHistoryPage?> LoadOlderAsync(int? beforeMessageId)
    {
        if (IsFullyLoaded || IsBusy)
            return null;

        IsBusy = true;
        try
        {
            if (beforeMessageId is { } beforeId)
            {
                var cached = await _cache.LoadBeforeAsync(beforeId, PageSize);
                if (cached is { Messages.Count: > 0 })
                {
                    IsFullyLoaded = cached.ReachesStart;
                    return new ChatHistoryPage(cached.Messages, cached.Revision);
                }
            }

            var (result, page) = await _coordinatorSession.RequestChatHistoryPage(_chatId, beforeMessageId, PageSize);
            if (!result.IsSuccess || page == null)
                return null;

            if (page.Messages.Count < PageSize)
                IsFullyLoaded = true;

            if (beforeMessageId is { } oldestId)
                _cache.AddOlder(oldestId, page.Messages, IsFullyLoaded);
            else
                _cache.Replace(page.Messages, page.Revision, IsFullyLoaded);

            return page;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Forgets that the history was exhausted, for loading it anew.
    /// </summary>
    public void Reset() => IsFullyLoaded = false;
}
