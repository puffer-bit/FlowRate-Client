using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Services;
using Shared.Enums;
using Shared.Validation;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Keeps the loaded messages current: notifications are applied as they come, and what they missed
/// is asked for by the revision of the chat.
/// </summary>
public partial class ActiveChatSessionVM
{
    private readonly ChatHistoryPager _pager;
    // The same messages kept between runs; follows every change the chat learns of, shown or not
    private readonly IChatCache _cache;

    // The revision of the chat the loaded messages are current at; touched only on the UI thread
    private long _revision;
    // The highest revision a notification carried, to notice what the first page did not cover
    private long _seenRevision;
    private bool _isSyncing;
    private bool _isSyncRequested;
    private bool _wasDisconnected;

    /// <summary>
    /// Shows the newest page. What the cache holds is shown at once and then brought up to date;
    /// a chat that is not cached, or whose cache is past use, waits for the server.
    /// </summary>
    private async Task LoadInitialPage(bool fromCache)
    {
        var cached = fromCache ? await _pager.LoadCachedAsync() : null;
        var page = cached ?? await _pager.LoadOlderAsync(null);
        await UiThread.RunAsync(() =>
        {
            if (page != null)
            {
                AddMessages(page.Messages);
                _revision = page.Revision;
            }

            IsInitialLoading = false;

            // The cache is as old as the last run. A page from the server may still be older than
            // a notification that came while it was on its way
            if (cached != null || _seenRevision > _revision)
                RequestSync();
        });
    }

    private void OnTextMessageAdded(int chatId, ChatMessageModel message)
    {
        if (chatId != Id)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            TrackRevision(message.Revision);
            _cache.Apply([message], _revision);
            if (AddMessages([message], isNew: true) == 0)
                return;

            // Your own message is always brought into view; others only add to the counter when out of sight
            if (message.IsMine)
                ScrollToEnd();
            else if (IsScrolledUp)
                UnreadBelowCount++;
        });
    }

    private void OnTextMessageEdited(int chatId, ChatMessageModel message)
    {
        if (chatId != Id)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            TrackRevision(message.Revision);
            _cache.Apply([message], _revision);
            ApplyChanges([message]);
        });
    }

    private void OnTextMessageDeleted(int chatId, int messageId, long revision)
    {
        if (chatId != Id)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            TrackRevision(revision);
            _cache.Remove(messageId, _revision);
            RemoveMessage(messageId);
        });
    }

    // Raised on the socket thread
    private void OnCoordinatorStateChanged(CoordinatorState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = state == CoordinatorState.Connected;
            if (!IsConnected)
            {
                _wasDisconnected = true;
                return;
            }

            if (!_wasDisconnected)
                return;

            // Notifications sent while the connection was down are lost, so ask what they were about
            _wasDisconnected = false;
            if (!IsInitialLoading)
                RequestSync();
        });
    }

    /// <summary>
    /// Follows the revision a notification carried. Revisions come one by one, so a jump means
    /// notifications were missed and what they changed has to be asked for.
    /// </summary>
    private void TrackRevision(long revision)
    {
        _seenRevision = Math.Max(_seenRevision, revision);
        if (IsInitialLoading || revision <= _revision)
            return;

        if (revision == _revision + 1)
            _revision = revision;
        else
            RequestSync();
    }

    private void RequestSync()
    {
        if (_isSyncing)
        {
            _isSyncRequested = true;
            return;
        }

        _ = SyncAsync();
    }

    /// <summary>
    /// Brings the loaded messages up to the current revision of the chat.
    /// </summary>
    private async Task SyncAsync()
    {
        _isSyncing = true;
        try
        {
            bool again = false;
            do
            {
                _isSyncRequested = false;

                var (result, changes) = await _coordinatorSession.RequestChatChanges(Id, _revision,
                    Limits.ChatHistoryMaxTake);
                if (result != FetchMessagesResult.NoError || changes == null)
                    return;

                // More was missed than one answer holds: the newest page is loaded anew rather than
                // every missed message, which a long absence from a busy chat makes thousands of
                if (changes.Reset || changes.HasMore)
                {
                    await ReloadAsync();
                    again = _isSyncRequested;
                    continue;
                }

                await UiThread.RunAsync(() =>
                {
                    ApplyChanges(changes.Messages);

                    again = _isSyncRequested;
                    _revision = Math.Max(_revision, changes.Revision);
                    _cache.Apply(changes.Messages, _revision);
                });
            } while (again);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>
    /// Drops the loaded messages and loads the newest page from the server again, which replaces the cache too.
    /// </summary>
    private async Task ReloadAsync()
    {
        await UiThread.RunAsync(() =>
        {
            _messageVms.Clear();
            _pager.Reset();
            _seenRevision = 0;
            RebuildTimeline();
            IsInitialLoading = true;
        });

        await LoadInitialPage(fromCache: false);
    }
}
