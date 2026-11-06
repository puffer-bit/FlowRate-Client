using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using ReactiveUI;
using Shared.Enums.User;

namespace Client.ViewModels.Sessions.Chat;

public partial class ActiveChatSessionVM
{
    public AvaloniaList<ChatPinnedMessageVM> PinnedMessages { get; } = new();
    public bool HasPinnedMessages => PinnedMessages.Count > 0;
    public IEnumerable<ChatPinnedMessageVM> VisiblePinnedMessages => StatusBar.IsPinsOpen
        ? PinnedMessages : Array.Empty<ChatPinnedMessageVM>();
    public string PinsCaption => Strings.Format("chat.pinned.count", ("count", PinnedMessages.Count.ToString()));
    public bool IsLoadingPinnedMessages => _loadingPins;
    public bool HasPinnedMessagesError
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsPinnedListEmpty));
        }
    }
    public bool IsPinnedListEmpty => !HasPinnedMessages && !IsLoadingPinnedMessages && !HasPinnedMessagesError;

    private void OnPinsPanelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ChatStatusBarVM.IsPinsOpen)) return;
        this.RaisePropertyChanged(nameof(VisiblePinnedMessages));
        if (StatusBar.IsPinsOpen) _ = LoadPinnedMessagesAsync();
    }
    private readonly Dictionary<int, ChatMessageModel> _pins = new();
    private bool _loadingPins;
    private bool _reloadPins;

    private async Task LoadPinnedMessagesAsync()
    {
        if (_isDisposed) return;
        if (_loadingPins) { _reloadPins = true; return; }
        _loadingPins = true;
        HasPinnedMessagesError = false;
        this.RaisePropertyChanged(nameof(IsLoadingPinnedMessages));
        try
        {
            do
            {
                _reloadPins = false;
                var (result, page) = await _coordinatorSession.RequestChatPinnedMessages(Id);
                if (_isDisposed) return;
                if (!result.IsSuccess || page == null)
                {
                    HasPinnedMessagesError = true;
                    return;
                }
                _pins.Clear();
                foreach (var message in CurrentPage(page.Messages))
                    if (message.IsPinned) _pins[message.Id] = message;
                foreach (var message in _messageChanges.Values)
                    if (message.Revision > page.Revision && message.IsPinned && !_deletedMessages.ContainsKey(message.Id))
                        _pins[message.Id] = message;
                PublishPins();
            } while (_reloadPins);
        }
        finally
        {
            _loadingPins = false;
            this.RaisePropertyChanged(nameof(IsLoadingPinnedMessages));
            this.RaisePropertyChanged(nameof(IsPinnedListEmpty));
        }
    }

    private void ApplyPinnedChanges(IReadOnlyList<ChatMessageModel> changes)
    {
        bool changed = false;
        foreach (var message in changes)
        {
            if (message.IsPinned && !message.IsDeleted && !_deletedMessages.ContainsKey(message.Id))
            {
                if (!_pins.TryGetValue(message.Id, out var previous) || message.Revision >= previous.Revision)
                { _pins[message.Id] = message; changed = true; }
            }
            else if (!_pins.TryGetValue(message.Id, out var previous) || message.Revision >= previous.Revision)
                changed |= _pins.Remove(message.Id);
        }
        if (changed) PublishPins();
    }

    private void RemovePinnedMessage(int id)
    {
        if (_pins.Remove(id)) PublishPins();
    }

    private void PublishPins()
    {
        var existing = PinnedMessages.ToDictionary(row => row.Id);
        var ordered = new List<ChatPinnedMessageVM>();
        foreach (var message in _pins.Values.OrderByDescending(x => x.PinnedAt).ThenByDescending(x => x.Id))
        {
            if (!existing.TryGetValue(message.Id, out var row))
                row = new ChatPinnedMessageVM(CreateMessageView(message), () => JumpToMessage(message.Id));
            else if (message.Revision > row.Content.Message.Revision)
                row.Content.Message = message;
            _senders.Apply(row.Content);
            ordered.Add(row);
        }
        // Keep media views attached when only names, previews or other pin rows change.
        for (int index = PinnedMessages.Count - 1; index >= 0; index--)
            if (!_pins.ContainsKey(PinnedMessages[index].Id)) PinnedMessages.RemoveAt(index);
        for (int index = 0; index < ordered.Count; index++)
        {
            var row = ordered[index];
            if (index < PinnedMessages.Count && ReferenceEquals(PinnedMessages[index], row)) continue;
            PinnedMessages.Remove(row);
            PinnedMessages.Insert(index, row);
        }
        this.RaisePropertyChanged(nameof(HasPinnedMessages));
        this.RaisePropertyChanged(nameof(PinsCaption));
        this.RaisePropertyChanged(nameof(IsPinnedListEmpty));
        var mentioned = new HashSet<int>();
        foreach (var row in PinnedMessages) ChatSenderDirectory.CollectUserIds(row.Content, mentioned);
        if (mentioned.Count > 0) _ = _senders.ResolveAsync(mentioned);
    }
}

public sealed class ChatPinnedMessageVM
{
    public ChatMessageVM Content { get; }
    public int Id => Content.Message.Id;
    public string Preview => Content.Body?.Text ?? Strings.Get("chat.pinned.attachment");
    private readonly Func<Task> _open;
    public void Open() => _ = _open();
    public void Unpin() => _ = Content.UnpinAsync();
    public ChatPinnedMessageVM(ChatMessageVM content, Func<Task> open)
    {
        Content = content;
        _open = open;
    }
}
