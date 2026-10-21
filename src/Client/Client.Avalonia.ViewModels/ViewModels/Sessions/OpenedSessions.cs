using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.ViewModels.Sessions;

/// <summary>
/// The sessions opened on one coordinator, alive whether they are shown or not.
/// Recently opened chats are kept, so returning to one shows its messages at once; the hybrid
/// session we are in keeps running in the background, and only one runs at a time.
/// Chats stay through a reconnect and catch up on their own, while the hybrid session is left,
/// since its media transport does not survive a lost connection.
/// Touched only on the UI thread.
/// </summary>
public sealed class OpenedSessions : ReactiveObject, IDisposable
{
    private const int ChatCapacity = 10;

    public ActiveHybridSessionVM? HybridSession
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsInHybridSession));
            UpdateHybridPanel(value);
            HybridSessionChanged?.Invoke(value);
        }
    }

    public bool IsInHybridSession => HybridSession != null;

    /// <summary>
    /// The session the side panel shows: the one we are in, and for the time the panel takes to
    /// leave, the one we have just left, so that the panel does not go blank while it folds.
    /// </summary>
    public ActiveHybridSessionVM? HybridPanelSession
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsHybridPanelLeaving
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public event Action<ActiveHybridSessionVM?>? HybridSessionChanged;

    /// <summary>
    /// Raised right before a session that ended is disposed, so whoever shows it can let go of it.
    /// </summary>
    public event Action<IMainWindowContentVM>? SessionClosed;

    private readonly ICoordinatorSession _coordinatorSession;
    private readonly ChatSessionServices _chatServices;
    private readonly ChatReadTracker _readTracker;
    private readonly Func<HybridSessionModel, ActiveHybridSessionVM> _createHybridSession;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;

    // The most recently opened chat is the last one
    private readonly List<ActiveChatSessionVM> _chats = new();

    public OpenedSessions(ICoordinatorSession coordinatorSession,
        ChatSessionServices chatServices,
        ChatReadTracker readTracker,
        Func<HybridSessionModel, ActiveHybridSessionVM> createHybridSession,
        ReactiveCommand<int, RxVoid> viewProfileCommand)
    {
        _coordinatorSession = coordinatorSession;
        _chatServices = chatServices;
        _readTracker = readTracker;
        _createHybridSession = createHybridSession;
        _viewProfileCommand = viewProfileCommand;

        _coordinatorSession.ChatSessionDeleted += OnChatSessionDeleted;
        _coordinatorSession.HybridSessionDeleted += OnHybridSessionDeleted;
    }

    /// <summary>
    /// Returns the session to show, opening it first when it is not open yet.
    /// </summary>
    public IMainWindowContentVM? Open(SessionModel sessionModel) => sessionModel switch
    {
        ChatSessionModel chatModel => OpenChat(chatModel),
        HybridSessionModel hybridModel => OpenHybridSession(hybridModel),
        _ => null
    };

    public void LeaveHybridSession()
    {
        var session = HybridSession;
        if (session == null)
            return;

        session.Closed -= OnHybridSessionClosed;
        HybridSession = null;
        SessionClosed?.Invoke(session);
        session.Dispose();
    }

    private void UpdateHybridPanel(ActiveHybridSessionVM? session)
    {
        if (session != null)
        {
            HybridPanelSession = session;
            IsHybridPanelLeaving = false;
            return;
        }

        var left = HybridPanelSession;
        if (left == null || IsHybridPanelLeaving)
            return;

        IsHybridPanelLeaving = true;
        DispatcherTimer.RunOnce(() =>
        {
            // Another session was joined in the meantime and the panel shows it now
            if (HybridPanelSession != left || !IsHybridPanelLeaving)
                return;

            HybridPanelSession = null;
            IsHybridPanelLeaving = false;
        }, Presence.LeaveTime);
    }

    private ActiveChatSessionVM OpenChat(ChatSessionModel chatModel)
    {
        var chat = _chats.Find(c => c.Id == chatModel.Id);
        if (chat != null)
            _chats.Remove(chat);
        else
            chat = new ActiveChatSessionVM(chatModel, _coordinatorSession, _chatServices, _readTracker,
                _viewProfileCommand);

        _chats.Add(chat);

        // The chat being opened is the newest, so it is never the one evicted
        if (_chats.Count > ChatCapacity)
            CloseChat(_chats[0]);

        return chat;
    }

    /// <summary>
    /// Closes the chats that are not among <paramref name="existingChatIds"/> and forgets what
    /// was cached of them: a deletion that happened while the connection was down was never announced.
    /// </summary>
    public void CloseChatsExcept(IReadOnlySet<int> existingChatIds)
    {
        _chatServices.Caches.RemoveExcept(_coordinatorSession.GetCurrentUser().CoordinatorInstanceId, existingChatIds);

        foreach (var chat in _chats.FindAll(c => !existingChatIds.Contains(c.Id)))
            CloseChat(chat);
    }

    private void CloseChat(ActiveChatSessionVM chat)
    {
        _chats.Remove(chat);
        SessionClosed?.Invoke(chat);
        chat.Dispose();
    }

    /// <summary>
    /// Returns to the session we are in, or leaves it and joins another one.
    /// </summary>
    private ActiveHybridSessionVM OpenHybridSession(HybridSessionModel hybridModel)
    {
        if (HybridSession?.Model.Id == hybridModel.Id)
            return HybridSession;

        LeaveHybridSession();

        var session = _createHybridSession(hybridModel);
        session.Closed += OnHybridSessionClosed;
        HybridSession = session;

        return session;
    }

    // The user left from the session itself, or joining failed
    private void OnHybridSessionClosed(ActiveHybridSessionVM session)
    {
        if (session == HybridSession)
            LeaveHybridSession();
    }

    // Deletions arrive on the socket thread
    private void OnChatSessionDeleted(int sessionId)
    {
        _chatServices.Caches.Remove(_coordinatorSession.GetCurrentUser().CoordinatorInstanceId, sessionId);

        Dispatcher.UIThread.Post(() =>
        {
            var chat = _chats.Find(c => c.Id == sessionId);
            if (chat != null)
                CloseChat(chat);
        });
    }

    private void OnHybridSessionDeleted(int sessionId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (HybridSession?.Model.Id == sessionId)
                LeaveHybridSession();
        });
    }

    public void Dispose()
    {
        _coordinatorSession.ChatSessionDeleted -= OnChatSessionDeleted;
        _coordinatorSession.HybridSessionDeleted -= OnHybridSessionDeleted;

        LeaveHybridSession();

        foreach (var chat in _chats)
            chat.Dispose();
        _chats.Clear();
    }
}
