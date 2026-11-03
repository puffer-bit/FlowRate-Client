using System;
using System.Linq;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Client.Views.Common;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Chat;
using Client.Avalonia.Controls.Emoji;
using Client.Core.Chat;
using Client.Tools.AppInfo;
using Client.Avalonia.Localization;

namespace Client.Views.Sessions.Chat;

/// <summary>
/// The box a message is written in: the reply bar, the attached files, the input with its buttons and the
/// notice of what keeps the message from going out.
/// </summary>
internal partial class ChatComposer : ViewControl<ChatComposerVM>
{
    private static readonly Lazy<RecentEmoji> Recent = new(LoadRecentEmoji);
    private Flyout EmojiFlyout => (Flyout)FlyoutBase.GetAttachedFlyout(EmojiButton)!;
    private static string RecentPath => Path.Combine(AppDirectories.Data, "recent-emoji.txt");
    private int _emojiSelectionStart;
    private int _emojiSelectionEnd;
    private ChatComposerVM? _emojiComposer;
    private TopLevel? _emojiTopLevel;

    public ChatComposer()
    {
        InitializeComponent();

        // Capture before the button or search takes focus; SelectedText preserves normal TextBox editing/undo.
        EmojiButton.AddHandler(PointerPressedEvent, (_, _) => CaptureEmojiSelection(), RoutingStrategies.Tunnel);
        EmojiFlyout.Opened += (_, _) =>
        {
            _emojiTopLevel = TopLevel.GetTopLevel(this);
            if (_emojiTopLevel != null) _emojiTopLevel.SizeChanged += OnEmojiWindowSizeChanged;
            SizeEmojiPicker();
            EmojiPicker.Open(Recent.Value);
        };
        EmojiFlyout.Closed += (_, _) =>
        {
            if (_emojiTopLevel != null) _emojiTopLevel.SizeChanged -= OnEmojiWindowSizeChanged;
            _emojiTopLevel = null;
        };
        SizeChanged += (_, _) => { if (EmojiFlyout.IsOpen) SizeEmojiPicker(); };
        DetachedFromVisualTree += (_, _) => EmojiFlyout.Hide();
        DataContextChanged += (_, _) => EmojiFlyout.Hide();
        EmojiPicker.CloseRequested += (_, _) =>
        {
            EmojiFlyout.Hide();
            MessageInput.Focus();
        };
        EmojiPicker.EmojiPicked += OnEmojiPicked;
        EmojiPicker.RecentChanged += (_, _) => SaveRecentEmoji();

        // Tunnel: a TextBox that accepts returns inserts the line break itself before the event bubbles up
        MessageInput.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        MessageInput.AddHandler(KeyDownEvent, OnInputPreviewKeyDown, RoutingStrategies.Tunnel);

        // The mention list follows the word at the caret
        MessageInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)
                ViewModel?.MentionPicker.Update(MessageInput.Text, MessageInput.CaretIndex);
        };
    }

    private void CaptureEmojiSelection()
    {
        _emojiSelectionStart = MessageInput.SelectionStart;
        _emojiSelectionEnd = MessageInput.SelectionEnd;
        _emojiComposer = ViewModel;
    }

    private void OnEmojiButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!MessageInput.IsEnabled || MessageInput.IsReadOnly) return;
        // Keyboard activation does not have a preceding pointer press.
        if (EmojiFlyout.IsOpen)
        {
            EmojiFlyout.Hide();
            return;
        }
        CaptureEmojiSelection();
        EmojiFlyout.ShowAt(EmojiButton);
    }

    private void OnEmojiWindowSizeChanged(object? sender, SizeChangedEventArgs e) => SizeEmojiPicker();

    private void SizeEmojiPicker()
    {
        var window = _emojiTopLevel?.ClientSize ?? Bounds.Size;
        EmojiPicker.Width = Math.Max(36, Math.Min(360, Math.Min(Bounds.Width, window.Width - 24) - 12));
        EmojiPicker.Height = Math.Max(100, Math.Min(380, window.Height * 0.65));
    }

    private void OnEmojiPicked(object? sender, EmojiPickedEventArgs e)
    {
        if (!ReferenceEquals(_emojiComposer, ViewModel) || !MessageInput.IsEnabled || MessageInput.IsReadOnly)
        {
            EmojiFlyout.Hide();
            return;
        }

        var text = MessageInput.Text ?? "";
        int start = Math.Clamp(Math.Min(_emojiSelectionStart, _emojiSelectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(_emojiSelectionStart, _emojiSelectionEnd), start, text.Length);
        // Reject a whole sequence at the limit, instead of allowing TextBox to truncate a surrogate or joiner.
        if (MessageInput.MaxLength > 0 && text.Length - (end - start) + e.Entry.Text.Length > MessageInput.MaxLength)
        {
            e.RejectionReason = Strings.Get("chat.emoji.limit");
            return;
        }

        MessageInput.SelectionStart = start;
        MessageInput.SelectionEnd = end;
        MessageInput.SelectedText = e.Entry.Text;
        MessageInput.CaretIndex = start + e.Entry.Text.Length;
        // Keep the picker focused and continue the next insertion after this whole sequence.
        _emojiSelectionStart = _emojiSelectionEnd = MessageInput.CaretIndex;
        MessageInput.SelectionStart = MessageInput.SelectionEnd = MessageInput.CaretIndex;
        e.Accepted = true;
    }

    private static RecentEmoji LoadRecentEmoji()
    {
        try { return new RecentEmoji(File.ReadLines(RecentPath)); }
        catch (IOException) { return new RecentEmoji(); }
        catch (UnauthorizedAccessException) { return new RecentEmoji(); }
    }

    private static void SaveRecentEmoji()
    {
        try
        {
            Directory.CreateDirectory(AppDirectories.Data);
            File.WriteAllLines(RecentPath, Recent.Value.Entries.Select(entry => entry.Text));
        }
        catch (IOException) { /* Picking still works when the profile cannot be written. */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Moves the keyboard focus to the input, so that a message can be typed right away.
    /// </summary>
    public void FocusInput() => MessageInput.Focus();

    /// <summary>
    /// Focuses the input with the caret after the text, for a text that was put there to be edited.
    /// </summary>
    public void FocusInputAtEnd()
    {
        MessageInput.Focus();
        MessageInput.CaretIndex = MessageInput.Text?.Length ?? 0;
    }

    /// <summary>
    /// Pasting attaches what the clipboard holds as files or as a picture; anything else is pasted
    /// into the input as usual.
    /// </summary>
    private async void OnInputPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (ViewModel is not { } vm || topLevel?.Clipboard is not { } clipboard
            || global::Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration.Paste
                .Any(gesture => gesture.Matches(e)) != true)
            return;

        // Decided before the clipboard is read, which takes time: the input must not paste meanwhile
        e.Handled = true;
        try
        {
            if (await clipboard.TryGetFilesAsync() is { Length: > 0 } files)
            {
                vm.AddAttachments(await PickedFiles.FromAsync(files));
                return;
            }

            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap != null)
            {
                vm.AddAttachments([PickedFiles.FromBitmap(bitmap, "image.png")]);
                return;
            }
        }
        catch (Exception)
        {
            // A clipboard that cannot be read as files or a picture is pasted as text
        }

        MessageInput.Paste();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel?.MentionPicker is { IsOpen: true } picker && HandleMentionKey(picker, e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && ViewModel is { IsReplyAttached: true } or { IsEditingMessage: true })
        {
            ObservableExtensions.Subscribe(ViewModel.ResetReplyMessageCommand.Execute(RxVoid.Default));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (ViewModel != null)
                ObservableExtensions.Subscribe(ViewModel.SendMessageCommand.Execute(RxVoid.Default));
            e.Handled = true;
        }
    }

    private bool HandleMentionKey(ChatMentionPickerVM picker, Key key)
    {
        switch (key)
        {
            case Key.Up:
                picker.MoveSelection(-1);
                return true;
            case Key.Down:
                picker.MoveSelection(1);
                return true;
            case Key.Enter or Key.Tab:
                CompleteMention(picker, null);
                return true;
            case Key.Escape:
                picker.Close();
                return true;
            default:
                return false;
        }
    }

    private void OnMentionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.MentionPicker is { } picker && sender is Button { DataContext: ChatMentionSuggestionVM suggestion })
            CompleteMention(picker, suggestion);
    }

    private void CompleteMention(ChatMentionPickerVM picker, ChatMentionSuggestionVM? suggestion)
    {
        if (picker.Complete(MessageInput.Text, MessageInput.CaretIndex, suggestion) is not { } completed)
            return;

        MessageInput.Text = completed.Text;
        MessageInput.CaretIndex = completed.CaretIndex;
        MessageInput.Focus();
    }
}
