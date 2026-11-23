using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Client.Avalonia.Localization;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Controls.Panels;
using Client.Avalonia.Styles.Animation;
using Client.Avalonia.Controls.Images;
using Client.Core.Settings;
using Client.Services;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Dialogs;
using Client.Droid.Platform;
using Client.Droid.Views.Common;
using ReactiveUI;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Reactions;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Droid.Views.Sessions.Chat;

/// <summary>
/// A chat on the whole screen. The view is one for every chat: it follows the chat it is given, asks
/// for older messages near the top, follows new messages while the user reads the end and reports how
/// far the end is. Holding the rows in place is the work of <see cref="MessageListPanel"/>.
/// </summary>
public partial class ChatView : UserControl
{
    // A tap on an attachment opens what is done to it; its remove button does its own
    private void OnAttachmentTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control card || e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) != null)
            return;

        e.Handled = true;
        if (PopupMenus.GetContextMenu(card) is { } menu)
            PopupMenus.Show(card, menu);
    }

    private void OnDetachClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChatAttachmentVM attachment } && DataContext is ActiveChatSessionVM chat)
            chat.Composer.RemoveAttachmentCommand.Execute(attachment).Subscribe();
    }

    private readonly Client.Core.Chat.RecentEmoji _recentEmoji = new();
    private Flyout? _emojiPickerFlyout;

    /// <summary>Gives the reaction a double tap puts on a message; set by the root view.</summary>
    public Func<Task<string>>? QuickReaction { get; set; }

    private void OnEmojiPickerOpened(object? sender, EventArgs e)
    {
        _emojiPickerFlyout = sender as Flyout;
        AllEmoji.Open(_recentEmoji);
    }

    private async void OnMessageDoubleTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Visual;
        // A double tap on what has a tap of its own, such as an image or a reaction, is two taps on it
        if (source?.GetSelfAndVisualAncestors().TakeWhile(visual => visual != MessagesList)
                .Any(visual => visual is Button) == true)
            return;

        var message = source?.GetSelfAndVisualAncestors().OfType<StyledElement>()
            .Select(element => element.DataContext).OfType<ChatMessageVM>().FirstOrDefault();
        if (message is not { IsSystem: false, CanReact: true } || QuickReaction is not { } quickReaction)
            return;

        // The first tap was waiting to open the menu: this one is a reaction instead
        _pendingTapMenu?.Cancel();
        e.Handled = true;
        var emoji = await quickReaction();
        message.ToggleReaction(ChatReactionEmoji.IsSupported(emoji) ? emoji : BehaviorSettings.DefaultQuickReaction);
    }

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
    private ChatMentionPickerVM? _mentionPicker;

    public ChatView()
    {
        InitializeComponent();

        MessageMenu.Dismissed += () => TryCloseMenu();

        // A message swiped to the left is replied to
        _ = new DragGesture(MessagesList, Orientation.Horizontal)
        {
            CanStart = (source, offset) => offset < 0 && BeginReplySwipe(source),
            Moved = MoveReplySwipe,
            Ended = (_, _) => EndReplySwipe()
        };
        ViewersPanel.CloseRequested += () => TryCloseViewers();

        // The mention list follows the word at the caret
        MessageInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)
                UpdateMentionPicker();
        };
        MessageInput.GotFocus += (_, _) => UpdateMentionPicker();
        MessageInput.LostFocus += (_, _) => Chat?.Composer.MentionPicker.Close();
        DetachedFromVisualTree += (_, _) => Chat?.Composer.MentionPicker.Close();
        DataContextChanged += (_, _) =>
        {
            _mentionPicker?.Close();
            _mentionPicker = Chat?.Composer.MentionPicker;
        };

        // Any emoji, for the message of the open menu; choosing one closes the picker and the menu
        AllEmoji.EmojiPicked += async (_, e) =>
        {
            if (MessageMenu.DataContext is not ChatMessageVM { CanReact: true } message)
                return;
            e.Accepted = true;
            _emojiPickerFlyout?.Hide();
            await message.ChooseReactionAsync(e.Entry.Text);
        };
        AllEmoji.CloseRequested += (_, _) => _emojiPickerFlyout?.Hide();

        // A double tap on a message puts the reaction chosen in the settings, or takes it back
        MessagesList.AddHandler(DoubleTappedEvent, OnMessageDoubleTapped);

        // A tap opens the menu of a message, and so does a long press, as the right button does with a mouse
        MessagesList.AddHandler(TappedEvent, OnMessageTapped);
        MessagesList.AddHandler(ContextRequestedEvent, OnMessageContextRequested);
        MessagesList.AddHandler(PointerPressedEvent, OnMessagesPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        MessagesList.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => _lastScrollTime = Environment.TickCount64);

        // The panel is built with the template of the list, so its event is caught on the list
        MessagesList.AddHandler(MessageListPanel.ScrollStateChangedEvent, (_, _) => OnScrollStateChanged());
    }

    /// <summary>
    /// Raised by the back button of the header.
    /// </summary>
    public event Action? BackRequested;

    /// <summary>
    /// Takes the menu of a message out of the chat, for the root view to show it over the whole screen.
    /// The chat keeps opening and closing it.
    /// </summary>
    public Control DetachMessageMenu()
    {
        (MessageMenu.Parent as Panel)?.Children.Remove(MessageMenu);
        return MessageMenu;
    }

    private void OpenChatDetails()
    {
        if (Chat?.StatusBar is { } statusBar)
        {
            if (statusBar.IsDirect)
                statusBar.OpenInfoCommand.Execute().Subscribe();
            else
                statusBar.Viewers?.Open();
        }
    }

    private void OnViewersClick(object? sender, RoutedEventArgs e) => Chat?.StatusBar.Viewers?.Open();

    private void OnPinsClick(object? sender, RoutedEventArgs e)
    {
        if (Chat is { } chat)
            chat.StatusBar.IsPinsOpen = true;
    }

    public bool TryCloseViewers()
    {
        if (Chat?.StatusBar.Viewers is not { IsOpen: true } viewers)
            return false;

        viewers.Close();
        return true;
    }

    private void OnChatTitleClick(object? sender, RoutedEventArgs e) => OpenChatDetails();

    private void OnChatTitleTapped(object? sender, TappedEventArgs e) => OpenChatDetails();

    private ActiveChatSessionVM? Chat => DataContext as ActiveChatSessionVM;

    private void UpdateMentionPicker()
    {
        if (MessageInput.IsFocused)
            Chat?.Composer.MentionPicker.Update(MessageInput.Text, MessageInput.CaretIndex);
        else
            Chat?.Composer.MentionPicker.Close();
    }

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
        if (OpenMessageMenu(e.Source as Visual))
            e.Handled = true;
    }

    // A press that stops the list while it still scrolls on is not a tap on a message
    private const long ScrollSettleMilliseconds = 150;
    private long _lastScrollTime;
    private bool _isPressDuringScroll;
    private CancellationTokenSource? _pendingTapMenu;
    // How long a tap waits for a second one, about the double tap time of Android
    private static readonly TimeSpan DoubleTapWait = TimeSpan.FromMilliseconds(300);

    private void OnMessagesPressed(object? sender, PointerPressedEventArgs e) =>
        _isPressDuringScroll = Environment.TickCount64 - _lastScrollTime < ScrollSettleMilliseconds;

    // A tap on a message opens its menu. What has a tap of its own keeps it: a button, such as an image,
    // a file, a reaction or the quoted message, a link or a mention in the text, the avatar and the name
    private async void OnMessageTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled || _isPressDuringScroll || e.Source is not Visual source || MessageMenu.IsOpen)
            return;

        var ancestors = source.GetSelfAndVisualAncestors().TakeWhile(visual => visual != MessagesList).ToList();
        if (ancestors.Any(visual => visual is Button or TextBox or SelectableTextBlock))
            return;

        if (ancestors.OfType<TextBlock>().FirstOrDefault(block => ChatMessageText.GetBody(block) != null) is { } text)
        {
            var position = e.GetPosition(text);
            if (ChatMessageText.GetLinkAt(text, position) != null || ChatMessageText.GetMentionAt(text, position) != null)
                return;
        }

        var message = ancestors.OfType<StyledElement>().Select(element => element.DataContext)
            .OfType<ChatMessageVM>().FirstOrDefault();
        if (message is not { IsSystem: false })
            return;

        e.Handled = true;
        _pendingTapMenu?.Cancel();

        // A second tap may follow for the quick reaction: the menu waits until it can no longer come
        if (message.CanReact && QuickReaction != null)
        {
            var pending = _pendingTapMenu = new CancellationTokenSource();
            try
            {
                await Task.Delay(DoubleTapWait, pending.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(_pendingTapMenu, pending))
                    _pendingTapMenu = null;
            }

            if (TopLevel.GetTopLevel(source) == null || MessageMenu.IsOpen)
                return;
        }

        OpenMessageMenu(source);
    }

    /// <returns>Whether the source is in a message; a system message has no menu.</returns>
    private bool OpenMessageMenu(Visual? source)
    {
        var message = source?.GetSelfAndVisualAncestors().OfType<StyledElement>()
            .Select(element => element.DataContext).OfType<ChatMessageVM>().FirstOrDefault();
        if (message == null)
            return false;
        if (message.IsSystem)
            return true;

        message.ContextFile = source?.GetSelfAndVisualAncestors().OfType<StyledElement>()
            .Select(element => element.DataContext).OfType<ChatFileVM>().FirstOrDefault();

        _pendingTapMenu?.Cancel();
        TryCloseMenu();
        var chat = Chat;

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
            }),
            message.SaveImageInteraction.RegisterHandler(async interaction =>
            {
                try
                {
                    await using var target = await PickSaveTarget(interaction.Input.Image.FileName);
                    if (target != null)
                        await interaction.Input.SaveOriginalAsync(target);
                }
                catch (Exception)
                {
                    if (chat != null)
                        await chat.ShowMessageBox.Handle(new MessageBoxVM(Icon.Error, Buttons.Ok,
                            Strings.Get("chat.image.saveFailed"), Strings.Get("chat.menu.saveFileAs")));
                }
                interaction.SetOutput(RxVoid.Default);
            })
        };

        // There is no selection to copy on a phone: the whole text is copied
        var text = message.Message.TextContent?.Text;
        message.SetCopyText(string.IsNullOrEmpty(text) ? null : text, false);
        message.IsFlyoutOpen = true;

        // The menu shows the message where it is in the list
        var row = source?.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(control => control.GetVisualParent() is MessageListPanel);
        if (row?.TransformToVisual(MessageMenu) is { } transform)
            MessageMenu.MessageBounds = new Rect(row.Bounds.Size).TransformToAABB(transform);

        MessageMenu.DataContext = message;
        MessageMenu.IsOpen = true;
        return true;
    }

    private void OnSelectMessageTextClick(object? sender, RoutedEventArgs e)
    {
        if (MessageMenu.DataContext is not ChatMessageVM message || string.IsNullOrEmpty(message.Message.TextContent?.Text))
            return;
        TryCloseMenu();
        NativeMessageText.Show(message.Message.TextContent.Text);
    }

    private CompositeDisposable Follow(ActiveChatSessionVM chat)
    {
        var subscriptions = new CompositeDisposable
        {
            Disposable.Create(() => chat.StatusBar.Viewers?.Close()),
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
                interaction.SetOutput(await PickFiles())),
            chat.PickFileSaveTarget.RegisterHandler(async interaction =>
                interaction.SetOutput(await PickSaveTarget(interaction.Input))),
            chat.OpenFileInteraction.RegisterHandler(async interaction =>
                interaction.SetOutput(await ChatFileLauncher.OpenAsync(this, interaction.Input)))
        };

        // The viewer of an image is made anew for each image; saving asks this view where to
        var viewerSubscription = new SerialDisposable();
        subscriptions.Add(viewerSubscription);
        subscriptions.Add(chat.WatchProperty(nameof(ActiveChatSessionVM.ImageViewer), x => x.ImageViewer)
            .Subscribe(viewer => viewerSubscription.Disposable = viewer?.PickSaveTarget.RegisterHandler(
                async interaction => interaction.SetOutput(await PickSaveTarget(interaction.Input)))));

        void FocusMention() => Dispatcher.UIThread.Post(() =>
        {
            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text?.Length ?? 0;
            MessageInput.SelectionStart = MessageInput.SelectionEnd = MessageInput.CaretIndex;
        }, DispatcherPriority.Background);
        chat.Composer.MentionInserted += FocusMention;
        subscriptions.Add(Disposable.Create(() => chat.Composer.MentionInserted -= FocusMention));

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

    // How far a message is swiped before letting go replies to it, and how far it goes at most
    private const double ReplySwipeDistance = 64;
    private const double ReplySwipeMax = 110;

    private Control? _swipedRow;
    private ChatMessageVM? _swipedMessage;
    private TranslateTransform? _swipeShift;
    private readonly ScaleTransform _replyIconScale = new();
    private bool _isReplyArmed;

    // The row of the pressed message, which a swipe moves
    private bool BeginReplySwipe(Visual? source)
    {
        if (Chat == null || MessageMenu.IsOpen)
            return false;

        var row = source?.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(control => control.GetVisualParent() is MessageListPanel);
        if (row?.DataContext is not ChatMessageVM { IsSystem: false } message)
            return false;

        _swipedRow = row;
        _swipedMessage = message;
        _isReplyArmed = false;
        _swipeShift = new TranslateTransform();
        row.RenderTransform = _swipeShift;

        // The icon stands beside the middle of the message
        var middle = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), MessagesList)?.Y ?? 0;
        var top = Math.Clamp(middle - ReplySwipeIcon.Height / 2, 0,
            Math.Max(0, MessagesList.Bounds.Height - ReplySwipeIcon.Height));
        ReplySwipeIcon.Margin = new Thickness(0, top, 12, 0);
        ReplySwipeIcon.RenderTransform = _replyIconScale;
        return true;
    }

    private void MoveReplySwipe(double offset)
    {
        if (_swipeShift == null)
            return;

        // Past the point of a reply the message follows the finger less and less
        var distance = Math.Max(0, -offset);
        var shift = distance <= ReplySwipeDistance
            ? distance
            : Math.Min(ReplySwipeMax, ReplySwipeDistance + (distance - ReplySwipeDistance) * 0.3);
        _swipeShift.X = -shift;

        var progress = Math.Min(1, shift / ReplySwipeDistance);
        ReplySwipeIcon.Opacity = progress;
        _replyIconScale.ScaleX = _replyIconScale.ScaleY = 0.6 + 0.4 * progress;

        // Felt once when letting go would reply, and undone by going back
        var isArmed = shift >= ReplySwipeDistance;
        if (isArmed && !_isReplyArmed)
            Haptics.Tick();
        _isReplyArmed = isArmed;
    }

    private async void EndReplySwipe()
    {
        if (_swipedRow is not { } row || _swipeShift is not { } shift)
            return;

        if (_isReplyArmed)
            _swipedMessage?.StartReply();

        _swipedRow = null;
        _swipedMessage = null;
        _swipeShift = null;
        _isReplyArmed = false;

        // The message springs back to its place
        var from = shift.X;
        var opacity = ReplySwipeIcon.Opacity;
        await DragGesture.Animate(row, Motion.Get(MotionDuration.Short), progress =>
        {
            shift.X = from * (1 - progress);
            if (_swipeShift == null)
                ReplySwipeIcon.Opacity = opacity * (1 - progress);
        });

        if (ReferenceEquals(row.RenderTransform, shift))
            row.RenderTransform = null;
    }

    // The picture follows a finger up or down while it is not zoomed, the rest fading as it goes; let go
    // far enough or flung, it flies off and closes
    private void OnImageViewerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Grid viewer || viewer.Tag is DragGesture
            || viewer.Children.OfType<ZoomableImage>().FirstOrDefault() is not { } image)
            return;

        var backdrop = viewer.Children.OfType<Button>().FirstOrDefault();
        var bar = viewer.Children.OfType<Border>().FirstOrDefault();
        var shift = new TranslateTransform();
        image.RenderTransform = shift;

        void Follow(double offset)
        {
            shift.Y = offset;
            var opacity = 1 - Math.Min(1, Math.Abs(offset) / Math.Max(1, viewer.Bounds.Height / 2));
            if (backdrop != null)
                backdrop.Opacity = opacity;
            if (bar != null)
                bar.Opacity = opacity;
        }

        void Restore()
        {
            shift.Y = 0;
            backdrop?.ClearValue(OpacityProperty);
            bar?.ClearValue(OpacityProperty);
        }

        viewer.Tag = new DragGesture(viewer, Orientation.Vertical)
        {
            // The bar has buttons of its own
            CanStart = (source, _) => bar == null || source == null || !(ReferenceEquals(source, bar) || bar.IsVisualAncestorOf(source)),
            Moved = Follow,
            Ended = async (offset, velocity) =>
            {
                var height = viewer.Bounds.Height;
                var isFlung = Math.Abs(velocity) > DragGesture.FlingVelocity && Math.Sign(velocity) == Math.Sign(offset);
                var duration = Motion.Get(MotionDuration.Short);
                if (Math.Abs(offset) > height / 5 || isFlung)
                {
                    var target = Math.Sign(offset) * height;
                    await DragGesture.Animate(viewer, duration, progress => Follow(offset + (target - offset) * progress));
                    Chat?.CloseImageViewer();
                    Restore();
                    return;
                }

                await DragGesture.Animate(viewer, duration, progress => Follow(offset * (1 - progress)));
                Restore();
            }
        };
    }

    /// <summary>Closes the pinned messages panel before Back leaves the chat.</summary>
    public bool TryClosePinnedMessages()
    {
        if (Chat?.StatusBar.IsPinsOpen != true) return false;
        Chat.StatusBar.IsPinsOpen = false;
        return true;
    }

    /// <summary>Closes the image opened over the chat, if one is.</summary>
    public bool TryCloseImageViewer()
    {
        if (Chat is not { IsImageViewerOpen: true } chat)
            return false;

        chat.CloseImageViewer();
        return true;
    }

    private async Task<IReadOnlyList<PickedFile>> PickFiles()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return [];

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            // The system picker takes one filter: any file, pictures among them
            FileTypeFilter = [PickedFiles.AnyFileType(Strings.Get("chat.attach.filter.all"))]
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

        chat.IsScrolledUp = chat.HasNewerMessages || panel.DistanceToEnd > ScrolledUpThreshold;

        if (panel.DistanceToStart < panel.ViewportHeight * PrefetchScreens)
            _ = LoadOlder(chat);

        if (panel.DistanceToEnd < panel.ViewportHeight * PrefetchScreens)
            _ = chat.LoadNewerMessagesAsync();
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
        if (Chat is { IsLoadingNewer: true })
            Panel?.HoldScrollPosition();

        // A message appended while the user reads the newest ones keeps the list at the bottom
        if (Chat is { IsScrolledUp: false } chat
            && e.Action == NotifyCollectionChangedAction.Add
            && e.NewStartingIndex + e.NewItems!.Count == chat.Messages.Count)
        {
            ScrollToEnd();
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void OnMentionPointerEntered(object? sender, PointerEventArgs e)
    {
        if (Chat?.Composer.MentionPicker is { } picker && sender is Button { DataContext: ChatMentionSuggestionVM suggestion })
            picker.SelectSuggestion(suggestion);
    }

    private void OnMentionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ActiveChatSessionVM { Composer.MentionPicker: var picker }
            || sender is not Button { DataContext: ChatMentionSuggestionVM suggestion }
            || picker.Complete(MessageInput.Text, MessageInput.CaretIndex, suggestion) is not { } completed)
            return;

        MessageInput.SelectAll();
        MessageInput.SelectedText = completed.Text;
        MessageInput.CaretIndex = completed.CaretIndex;
    }
}
