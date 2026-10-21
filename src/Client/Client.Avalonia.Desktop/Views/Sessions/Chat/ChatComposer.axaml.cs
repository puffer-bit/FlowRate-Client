using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Client.Views.Common;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

/// <summary>
/// The box a message is written in: the reply bar, the attached files, the input with its buttons and the
/// notice of what keeps the message from going out.
/// </summary>
internal partial class ChatComposer : ViewControl<ChatComposerVM>
{
    public ChatComposer()
    {
        InitializeComponent();

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
