using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using ReactiveUI;
using Client.Core.Sessions;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

public class ChatStatusBarVM : ReactiveObject, IDisposable
{
    public string Name => _session.Name;
    public ChatViewersVM? Viewers { get; init; }
    public Interaction<ChatInfoWindowVM, RxVoid> ShowInfo { get; } = new();
    public ReactiveCommand<RxVoid, RxVoid> OpenInfoCommand { get; }

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
        : IsLoading ? Strings.Get("chat.loadingMessages")
        : string.IsNullOrWhiteSpace(_session.Description) ? Strings.Get("session.type.chat") : _session.Description.Trim();

    public bool IsTyping => _typingNames.Count > 0;

    private IReadOnlyList<string> _typingNames = [];

    public void SetTypingNames(IReadOnlyList<string> names)
    {
        _typingNames = names;
        this.RaisePropertyChanged(nameof(IsTyping));
        UpdateTexts();
    }

    private ChatSessionModel _session;
    private readonly ReactiveCommand<RxVoid, RxVoid> _openInfoCommand;
    private bool _isInfoOpen;

    public ChatStatusBarVM(ChatSessionModel sessionModel)
    {
        _session = sessionModel;
        _openInfoCommand = ReactiveCommand.CreateFromTask(OpenInfoAsync);
        // Opening a dialog must not disable or dim the header for the dialog's lifetime.
        OpenInfoCommand = ReactiveCommand.Create(() =>
        {
            if (!_isInfoOpen)
                _openInfoCommand.Execute().Subscribe();
        });
    }

    public void ApplySession(ChatSessionModel session)
    {
        _session = session;
        this.RaisePropertyChanged(nameof(Name));
        UpdateTexts();
    }

    private async Task OpenInfoAsync()
    {
        if (_isInfoOpen)
            return;

        _isInfoOpen = true;
        try
        {
            using var info = new ChatInfoWindowVM(_session);
            await ShowInfo.Handle(info);
        }
        finally
        {
            _isInfoOpen = false;
        }
    }

    /// <summary>
    /// Puts the texts into the language that has just been chosen.
    /// </summary>
    public void UpdateTexts()
    {
        this.RaisePropertyChanged(nameof(StatusText));
        Viewers?.UpdateTexts();
    }

    public void Dispose()
    {
        OpenInfoCommand.Dispose();
        _openInfoCommand.Dispose();
        Viewers?.Dispose();
    }
}
