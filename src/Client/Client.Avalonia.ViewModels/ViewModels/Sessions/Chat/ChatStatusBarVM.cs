using System.Collections.Generic;
using Client.Avalonia.Localization;
using ReactiveUI;
using Client.Core.Sessions;

namespace Client.ViewModels.Sessions.Chat;

public class ChatStatusBarVM : ReactiveObject
{
    public string Name { get; }

    /// <summary>
    /// Messages are being fetched, either the first page or an older one.
    /// </summary>
    public bool IsLoading
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>
    /// The line under the chat name: what is going on right now, otherwise what the chat is about.
    /// </summary>
    public string StatusText => _typingNames.Count > 0
        ? Strings.Format(_typingNames.Count == 1 ? "chat.typing.one" : "chat.typing.many",
            ("names", string.Join(", ", _typingNames)))
        : IsLoading ? Strings.Get("chat.loadingMessages") : _description ?? Strings.Get("session.type.chat");

    public bool IsTyping => _typingNames.Count > 0;

    private IReadOnlyList<string> _typingNames = [];

    public void SetTypingNames(IReadOnlyList<string> names)
    {
        _typingNames = names;
        this.RaisePropertyChanged(nameof(IsTyping));
        UpdateTexts();
    }

    // Null without a description, then the type of the session is shown in the current language
    private readonly string? _description;

    public ChatStatusBarVM(ChatSessionModel sessionModel)
    {
        Name = sessionModel.Name;
        _description = string.IsNullOrWhiteSpace(sessionModel.Description) ? null : sessionModel.Description.Trim();
    }

    /// <summary>
    /// Puts the texts into the language that has just been chosen.
    /// </summary>
    public void UpdateTexts() => this.RaisePropertyChanged(nameof(StatusText));
}
