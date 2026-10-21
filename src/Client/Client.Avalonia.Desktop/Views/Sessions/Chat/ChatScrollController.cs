using System;
using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Client.Avalonia.Controls.Panels;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

/// <summary>
/// Connects the message list of <see cref="ChatSessionControl"/> with its chat: asks for older messages
/// near the top, follows new messages while the user reads the end and reports how far the end is.
/// Holding the rows in place is the work of <see cref="MessageListPanel"/>.
/// </summary>
internal sealed class ChatScrollController
{
    // Older messages are requested while the viewport is within this many screens of the top,
    // so the next page is usually in place before the user reaches it
    private const double PrefetchScreens = 1.5;

    // The scroll-down button shows once the newest message is further than this below the viewport
    private const double ScrolledUpThreshold = 150;

    private readonly ChatSessionControl _view;
    private readonly ItemsControl _list;

    private bool _isLoadingOlder;

    public ChatScrollController(ChatSessionControl view, ItemsControl list)
    {
        _view = view;
        _list = list;

        // The panel is built with the template of the list, so its event is caught on the list
        _list.AddHandler(MessageListPanel.ScrollStateChangedEvent, (_, _) => OnScrollStateChanged());
    }

    private MessageListPanel? Panel => _list.ItemsPanelRoot as MessageListPanel;

    /// <summary>
    /// Follows the messages of <paramref name="chat"/> until the returned subscription is disposed.
    /// </summary>
    public IDisposable Follow(ActiveChatSessionVM chat)
    {
        chat.Messages.CollectionChanged += OnMessagesChanged;
        return Disposable.Create(() => chat.Messages.CollectionChanged -= OnMessagesChanged);
    }

    public void ScrollToEnd()
    {
        Panel?.ScrollToEnd();

        // Known before the layout pass, so that a message that comes in the meantime is followed too
        if (_view.ViewModel is { } chat)
            chat.IsScrolledUp = false;
    }

    /// <summary>
    /// Scrolls so that the row at <paramref name="index"/> sits in the middle of the viewport.
    /// </summary>
    public void ScrollIndexToCenter(int index)
    {
        Panel?.ScrollToCenter(index);
    }

    private void OnScrollStateChanged()
    {
        if (Panel is not { } panel || _view.ViewModel is not { } chat)
            return;

        chat.IsScrolledUp = panel.DistanceToEnd > ScrolledUpThreshold;

        if (panel.DistanceToStart < panel.ViewportHeight * PrefetchScreens)
            _ = LoadOlder(chat);
    }

    // One page per call: its rows change the extent of the list, which reports the scroll state again,
    // so pages keep coming for as long as the viewport stays near the top
    private async Task LoadOlder(ActiveChatSessionVM chat)
    {
        if (_isLoadingOlder)
            return;

        _isLoadingOlder = true;
        try
        {
            await chat.LoadOlderMessagesAsync();
        }
        finally
        {
            _isLoadingOlder = false;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A message appended while the user reads the newest ones keeps the list at the bottom
        if (_view.ViewModel is { IsScrolledUp: false } chat
            && e.Action == NotifyCollectionChangedAction.Add
            && e.NewStartingIndex + e.NewItems!.Count == chat.Messages.Count)
        {
            ScrollToEnd();
        }
    }
}
