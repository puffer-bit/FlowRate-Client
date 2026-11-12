using System.Threading.Tasks;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Core.Chat;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Pages the live tail from the contiguous local cache, and historical windows in either direction
/// from the server. Exclusive message ID cursors remain stable across insertions and deletions.
/// Historical pages never replace or extend the cached tail across a gap.
/// </summary>
public class ChatHistoryPager
{
    public const int PageSize = 30;

    public bool IsFullyLoaded { get; private set; }
    public bool HasNewer { get; private set; }
    public bool IsHistoricalWindow { get; private set; }
    public int? OldestMessageId { get; private set; }
    public int? NewestMessageId { get; private set; }
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
            if (IsHistoricalWindow && beforeMessageId is { } boundary)
            {
                var (windowResult, window) = await _coordinatorSession.RequestChatHistoryWindow(_chatId,
                    boundary, Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection.Before, PageSize);
                if (!windowResult.IsSuccess || window == null) return null;
                IsFullyLoaded = !window.HasOlder;
                if (window.Messages.Count > 0) OldestMessageId = window.Messages[0].Id;
                return new ChatHistoryPage(window.Messages, window.Revision);
            }
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
    public void Reset()
    {
        IsFullyLoaded = false;
        HasNewer = false;
        IsHistoricalWindow = false;
        OldestMessageId = null;
        NewestMessageId = null;
    }

    public async Task<ChatHistoryWindow?> LoadAroundAsync(int messageId)
    {
        if (IsBusy) return null;
        IsBusy = true;
        try
        {
            var (result, window) = await _coordinatorSession.RequestChatHistoryWindow(_chatId, messageId,
                Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection.Around, PageSize);
            if (!result.IsSuccess || window == null) return null;
            IsHistoricalWindow = true;
            OldestMessageId = window.Messages.Count > 0 ? window.Messages[0].Id : messageId;
            NewestMessageId = window.Messages.Count > 0 ? window.Messages[^1].Id : messageId;
            IsFullyLoaded = !window.HasOlder;
            HasNewer = window.HasNewer;
            return window;
        }
        finally { IsBusy = false; }
    }

    public async Task<ChatHistoryWindow?> LoadNewerAsync(int afterMessageId)
    {
        if (IsBusy || !HasNewer) return null;
        IsBusy = true;
        try
        {
            var (result, window) = await _coordinatorSession.RequestChatHistoryWindow(_chatId, afterMessageId,
                Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection.After, PageSize);
            if (!result.IsSuccess || window == null) return null;
            HasNewer = window.HasNewer;
            if (window.Messages.Count > 0) NewestMessageId = window.Messages[^1].Id;
            return window;
        }
        finally { IsBusy = false; }
    }
}
