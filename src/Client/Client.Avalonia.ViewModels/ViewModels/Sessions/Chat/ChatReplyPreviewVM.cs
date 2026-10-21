using System;
using Client.Avalonia.Localization;
using ReactiveUI;
using Client.Core.Chat;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A one-line preview of the message being replied to, shown as a quote above a message
/// and in the reply bar above the input.
/// </summary>
public class ChatReplyPreviewVM : ReactiveObject
{
    public int MessageId { get; }

    // The text as the message has it, with mention tokens; Text is it as shown
    private string? _sourceText;
    private Func<string, string> _renderText = text => text;
    public int? SenderUserId { get; }
    public string Text
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Null until the author is resolved by the chat.
    /// </summary>
    public string? SenderName
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ReplyTitle));
        }
    }

    /// <summary>
    /// The author of the quoted message was deleted; the name is a placeholder.
    /// </summary>
    public bool IsSenderDeleted
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The bar above the input shows the message being edited instead of one being answered.
    /// </summary>
    public bool IsEdit { get; }

    /// <summary>
    /// The line of the reply bar above the input that tells whom the reply goes to,
    /// or that a message is being edited.
    /// </summary>
    public string ReplyTitle => IsEdit
        ? Strings.Get("chat.editing")
        : Strings.Format("chat.replyTo", ("name", SenderName ?? string.Empty));

    public bool IsDeleted
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ChatReplyPreviewVM(ChatReplyContent content)
        : this(content.MessageId, content.SenderUserId, content.TextContent?.Text, content.IsDeleted)
    {
    }

    public ChatReplyPreviewVM(ChatMessageModel message, bool isEdit = false)
        : this(message.Id, message.SenderUserId, message.TextContent?.Text, message.IsDeleted)
    {
        IsEdit = isEdit;
    }

    private ChatReplyPreviewVM(int messageId, int? senderUserId, string? text, bool isDeleted)
    {
        MessageId = messageId;
        SenderUserId = senderUserId;
        _sourceText = text;
        Text = ToPreviewText(text);
        IsDeleted = isDeleted;
    }

    /// <summary>
    /// Follows a change of the message this preview quotes.
    /// </summary>
    public void Update(ChatMessageModel message)
    {
        IsDeleted = message.IsDeleted;
        if (!message.IsDeleted)
        {
            _sourceText = message.TextContent?.Text;
            Text = ToPreviewText(_sourceText == null ? null : _renderText(_sourceText));
        }
    }

    /// <summary>
    /// Shows the mention tokens of the quoted text the way the chat writes them; called again when
    /// the names change.
    /// </summary>
    public void RenderText(Func<string, string> render)
    {
        _renderText = render;
        Text = ToPreviewText(_sourceText == null ? null : render(_sourceText));
    }

    /// <summary>
    /// The quoted text with its mention tokens, to look up the names it needs.
    /// </summary>
    public string? SourceText => _sourceText;

    private static string ToPreviewText(string? text)
    {
        // The preview is a single line, so line breaks would hide everything after the first one
        text = text?.ReplaceLineEndings(" ").Trim();
        // A message without text carries only media or files
        return string.IsNullOrEmpty(text) ? Strings.Get("chat.attachment") : text;
    }
}
