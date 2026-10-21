using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Client.Avalonia.Localization;
using Avalonia.Threading;
using Client.Avalonia.Controls.Panels;
using Client.Services;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Droid.Views;

/// <summary>
/// A chat on the whole screen. The view is one for every chat: it follows the chat it is given, asks
/// for older messages near the top, follows new messages while the user reads the end and reports how
/// far the end is. Holding the rows in place is the work of <see cref="MessageListPanel"/>.
/// </summary>
public partial class ChatView : UserControl
{
    // Older messages are requested while the viewport is within this many screens of the top,
    // so the next page is usually in place before the user reaches it
    private const double PrefetchScreens = 1.5;

    // The scroll-down button shows once the newest message is further than this below the viewport
    private const double ScrolledUpThreshold = 150;

    // Chats outlive the view, so the subscriptions to one end as soon as another one is shown
    private readonly SerialDisposable _chatSubscriptions = new();

    // What the open menu asks of the view; kept after the menu closes, since copying follows the close
    private readonly SerialDisposable _menuSubscriptions = new();

    private bool _isLoadingOlder;

    public ChatView()
    {
        InitializeComponent();

        MessageMenu.Dismissed += () => TryCloseMenu();

        // The mention list follows the word at the caret
        MessageInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)
                (DataContext as ActiveChatSessionVM)?.Composer.MentionPicker.Update(MessageInput.Text, MessageInput.CaretIndex);
        };

        // A long press asks for the context menu, as the right button does with a mouse
        MessagesList.AddHandler(ContextRequestedEvent, OnMessageContextRequested);

        // The panel is built with the template of the list, so its event is caught on the list
        MessagesList.AddHandler(MessageListPanel.ScrollStateChangedEvent, (_, _) => OnScrollStateChanged());
    }

    /// <summary>
    /// Raised by the menu button of the header.
    /// </summary>
    public event Action? MenuRequested;

    /// <summary>
    /// Takes the menu of a message out of the chat, for the root view to show it over the whole screen.
    /// The chat keeps opening and closing it.
    /// </summary>
    public Control DetachMessageMenu()
    {
        (MessageMenu.Parent as Panel)?.Children.Remove(MessageMenu);
        return MessageMenu;
    }

    private ActiveChatSessionVM? Chat => DataContext as ActiveChatSessionVM;

    private MessageListPanel? Panel => MessagesList.ItemsPanelRoot as MessageListPanel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // The menu is about a message of the chat that was shown
        TryCloseMenu();
        _chatSubscriptions.Disposable = Chat is { } chat ? Follow(chat) : null;
    }

    /// <summary>
    /// Closes the menu of a message when it is open.
    /// </summary>
    /// <returns>False when there was no menu to close.</returns>
    public bool TryCloseMenu()
    {
        if (!MessageMenu.IsOpen)
            return false;

        if (MessageMenu.DataContext is ChatMessageVM message)
            message.IsFlyoutOpen = false;

        MessageMenu.IsOpen = false;
        return true;
    }

    private void OnMessageContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not StyledElement { DataContext: ChatMessageVM message })
            return;

        e.Handled = true;
        TryCloseMenu();

        // Every item closes the menu before it acts
        _menuSubscriptions.Disposable = new CompositeDisposable
        {
            message.CloseFlyoutInteraction.RegisterHandler(interaction =>
            {
                TryCloseMenu();
                interaction.SetOutput(RxVoid.Default);
            }),
            message.CopyMessageInteraction.RegisterHandler(async interaction =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(interaction.Input);

                interaction.SetOutput(RxVoid.Default);
            })
        };

        // There is no selection to copy on a phone: the whole text is copied
        var text = message.Message.TextContent?.Text;
        message.SetCopyText(string.IsNullOrEmpty(text) ? null : text, false);
        message.IsFlyoutOpen = true;

        MessageMenu.DataContext = message;
        MessageMenu.IsOpen = true;
    }

    private CompositeDisposable Follow(ActiveChatSessionVM chat)
    {
        var subscriptions = new CompositeDisposable
        {
            chat.ScrollToEndInteraction.RegisterHandler(interaction =>
            {
                ScrollToEnd();
                interaction.SetOutput(RxVoid.Default);
            }),
            chat.ScrollToMessageIndexInteraction.RegisterHandler(interaction =>
            {
                Panel?.ScrollToCenter(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }),
            chat.Composer.PickAttachmentFiles.RegisterHandler(async interaction =>
                interaction.SetOutput(await PickImages()))
        };

        // The viewer of an image is made anew for each image; saving asks this view where to
        var viewerSubscription = new SerialDisposable();
        subscriptions.Add(viewerSubscription);
        subscriptions.Add(chat.WatchProperty(nameof(ActiveChatSessionVM.ImageViewer), x => x.ImageViewer)
            .Subscribe(viewer => viewerSubscription.Disposable = viewer?.PickSaveTarget.RegisterHandler(
                async interaction => interaction.SetOutput(await PickSaveTarget(interaction.Input)))));

        // Choosing Reply moves the caret to the input so the answer can be typed right away
        subscriptions.Add(chat.Composer.WatchProperty(nameof(ChatComposerVM.ReplyPreview), x => x.ReplyPreview)
            .Skip(1)
            .Where(reply => reply != null)
            .Subscribe(_ => Dispatcher.UIThread.Post(() => MessageInput.Focus(), DispatcherPriority.Background)));

        // Choosing Edit puts the text into the input; the caret goes after it
        subscriptions.Add(chat.Composer.WatchProperty(nameof(ChatComposerVM.EditMessage), x => x.EditMessage)
            .Skip(1)
            .Where(edited => edited != null)
            .Subscribe(_ => Dispatcher.UIThread.Post(() =>
            {
                MessageInput.Focus();
                MessageInput.CaretIndex = MessageInput.Text?.Length ?? 0;
            }, DispatcherPriority.Background)));

        chat.Messages.CollectionChanged += OnMessagesChanged;
        subscriptions.Add(Disposable.Create(() => chat.Messages.CollectionChanged -= OnMessagesChanged));

        // A chat opens at its newest message
        ScrollToEnd();

        return subscriptions;
    }

    /// <summary>
    /// Closes the image opened over the chat, if one is.
    /// </summary>
    public bool TryCloseImageViewer()
    {
        if (Chat is not { IsImageViewerOpen: true } chat)
            return false;

        chat.CloseImageViewer();
        return true;
    }

    private async Task<IReadOnlyList<PickedFile>> PickImages()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return [];

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = [PickedFiles.ImageFileType(Strings.Get("chat.attach.filter.images"))]
        });

        // The files stay open: they are read when the message is sent
        return await PickedFiles.FromAsync(files);
    }

    private async Task<Stream?> PickSaveTarget(string fileName)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = fileName });
        return file == null ? null : await file.OpenWriteAsync();
    }

    private void ScrollToEnd()
    {
        Panel?.ScrollToEnd();

        // Known before the layout pass, so that a message that comes in the meantime is followed too
        if (Chat is { } chat)
            chat.IsScrolledUp = false;
    }

    private void OnScrollStateChanged()
    {
        if (Panel is not { } panel || Chat is not { } chat)
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
        if (Chat is { IsScrolledUp: false } chat
            && e.Action == NotifyCollectionChangedAction.Add
            && e.NewStartingIndex + e.NewItems!.Count == chat.Messages.Count)
        {
            ScrollToEnd();
        }
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e) => MenuRequested?.Invoke();

    private void OnMentionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ActiveChatSessionVM { Composer.MentionPicker: var picker }
            || sender is not Button { DataContext: ChatMentionSuggestionVM suggestion }
            || picker.Complete(MessageInput.Text, MessageInput.CaretIndex, suggestion) is not { } completed)
            return;

        MessageInput.Text = completed.Text;
        MessageInput.CaretIndex = completed.CaretIndex;
    }
}
