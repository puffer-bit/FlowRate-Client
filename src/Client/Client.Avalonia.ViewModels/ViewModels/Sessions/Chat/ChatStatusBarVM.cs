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
    public string StatusText => IsLoading ? Strings.Get("chat.loadingMessages") : _description;

    private readonly string _description;

    public ChatStatusBarVM(ChatSessionModel sessionModel)
    {
        Name = sessionModel.Name;
        _description = string.IsNullOrWhiteSpace(sessionModel.Description) ? Strings.Get("session.type.chat") : sessionModel.Description.Trim();
    }
}
