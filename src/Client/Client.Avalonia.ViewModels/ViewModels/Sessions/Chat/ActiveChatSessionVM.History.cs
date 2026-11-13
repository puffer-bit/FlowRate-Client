using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Core.Chat;
using Client.Services;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

public partial class ActiveChatSessionVM
{
    private IEnumerable<ChatMessageModel> CurrentPage(IEnumerable<ChatMessageModel> messages)
    {
        foreach (var message in messages)
        {
            if (_deletedMessages.ContainsKey(message.Id)) continue;
            yield return _messageChanges.TryGetValue(message.Id, out var current) && current.Revision > message.Revision
                ? current : message;
        }
    }

    private void ClearHistory()
    {
        DisposeReaders(_messageVms.Values);
        _messageVms.Clear();
        _newestMessageId = 0;
        _timeline.Clear();
        this.RaisePropertyChanged(nameof(HasMessages));
    }

    public async Task<int> LoadNewerMessagesAsync()
    {
        if (IsInitialLoading || IsLoadingNewer || !HasNewerMessages || _isDisposed) return 0;
        IsLoadingNewer = true;
        await _historyGate.WaitAsync();
        try
        {
            if (_isDisposed || _pager.NewestMessageId is not { } boundary) return 0;
            var page = await _pager.LoadNewerAsync(boundary);
            if (page == null || _isDisposed) return 0;
            var added = 0;
            await UiThread.RunAsync(() =>
            {
                if (_isDisposed) return;
                // Keep following a visible anchor while this historical page is appended.
                IsScrolledUp = true;
                added = AddMessages(CurrentPage(page.Messages));
                if (!HasNewerMessages)
                    added += AddMessages(CurrentPage(_messageChanges.Values.Where(x => x.Revision > page.Revision
                        && x.Id > _newestMessageId)));
                HistoryBoundsChanged();
            });
            return added;
        }
        finally
        {
            IsLoadingNewer = false;
            _historyGate.Release();
        }
    }
}
