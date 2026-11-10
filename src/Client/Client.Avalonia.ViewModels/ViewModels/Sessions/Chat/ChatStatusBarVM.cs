using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using ReactiveUI;
using Client.Core.Sessions;
using Client.Core.Users;
using Shared.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

public class ChatStatusBarVM : ReactiveObject, IDisposable
{
    private UserModel? _peer;
    private readonly ReactiveCommand<int, RxVoid>? _viewProfile;
    public bool IsDirect => _session.Kind == ChatKind.Direct;
    public bool HasViewers => Viewers != null;
    public string Name => IsDirect ? _peer?.DisplayName ?? _peer?.Username ?? string.Empty : _session.Name;
    public string? AvatarUrl => _peer?.AvatarUrl;
    public Shared.Enums.User.UserStatus? PeerStatus => _peer?.Status;

    public void ApplyPeer(UserModel peer)
    {
        _peer = peer;
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(AvatarUrl));
        this.RaisePropertyChanged(nameof(PeerStatus));
    }
    public ChatViewersVM? Viewers
    {
        get;
        init
        {
            field = value;
            if (field != null) field.PropertyChanged += OnViewersChanged;
        }
    }
    public bool IsPinsOpen
    {
        get;
        set
        {
            // Keep the shared panel open while switching its content.
            this.RaiseAndSetIfChanged(ref field, value);
            if (value) Viewers?.Close();
            this.RaisePropertyChanged(nameof(IsSidePanelOpen));
        }
    }
    public bool IsSidePanelOpen => IsPinsOpen || Viewers?.IsOpen == true;

    private void OnViewersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ChatViewersVM.IsOpen)) return;
        if (Viewers?.IsOpen == true) IsPinsOpen = false;
        this.RaisePropertyChanged(nameof(IsSidePanelOpen));
    }
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

    public ChatStatusBarVM(ChatSessionModel sessionModel, ReactiveCommand<int, RxVoid>? viewProfile = null)
    {
        _session = sessionModel;
        _viewProfile = viewProfile;
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
        if (IsDirect)
        {
            if (_peer != null) _viewProfile?.Execute(_peer.Id).Subscribe();
            return;
        }
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
        if (Viewers != null) Viewers.PropertyChanged -= OnViewersChanged;
        Viewers?.Dispose();
    }
}
