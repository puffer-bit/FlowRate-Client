using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Sounds;
using Client.Core.Chat;
using Client.Core.Push;
using Client.Droid.Platform.Calls;
using Client.Droid.Platform.Notifications;
using Client.Mobile.Views.Main;
using Client.Services;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Droid.Platform;

/// <summary>
/// Ties the shared mobile root view to Android: the notifications of chats and calls, what a tapped
/// notification asks for, and the activity coming to the front or leaving it. Created once, with the view.
/// </summary>
internal sealed class AndroidMainViewIntegration
{
    private readonly MainWindowVM _viewModel;
    private readonly IServiceProvider _services;
    private bool _readyForCallActions;
    private bool _handlingCallAction;

    public AndroidMainViewIntegration(MainView view, MainWindowVM viewModel, IServiceProvider services)
    {
        _viewModel = viewModel;
        _services = services;

        // The chat of a tapped notification can be shown once the list of the server has it
        viewModel.SessionsList.WatchProperty(nameof(SessionsListVM.State), list => list.State)
            .Subscribe(_ => Dispatcher.UIThread.Post(() => ShowRequestedChat(isListFresh: true)));
        MainActivity.ChatRequested += () =>
        {
            ShowRequestedChat(isListFresh: false);
            _ = HandleRequestedCallAsync();
        };
        MainActivity.IsInFrontChanged += OnIsInFrontChanged;
        viewModel.ChatMessageReceived += OnChatMessageReceived;
        // During the background grace period a live socket can deliver the invitation before FCM.
        viewModel.CallNotificationReceived += payload =>
            IncomingCallNotifications.Handle(global::Android.App.Application.Context, payload);
        viewModel.UsesSystemIncomingCallSound = true;
        PushMessageHandler.IsCallAccountOnScreen = viewModel.IsCallAccountOnScreen;
        viewModel.IsWindowActive = MainActivity.IsInFront;
        viewModel.ChatReads.StateChanged += OnChatReadStateChanged;
        PushMessageHandler.ChatMarkedRead = (coordinatorId, chatId, messageId) => Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.ConnectedCoordinatorId == coordinatorId)
                viewModel.ChatReads.MarkRead(chatId, messageId);
        });

        // The chat on screen follows what the view shows, which follows the view model
        viewModel.WatchProperty(nameof(MainWindowVM.CurrentContent), vm => vm.CurrentContent)
            .Subscribe(_ => Dispatcher.UIThread.Post(UpdateChatOnScreen));

        // The voice session goes on off the screen, with a notification of its own
        _ = new CallTracker(viewModel);

        view.ApplicationLoaded += () =>
        {
            _readyForCallActions = true;
            UpdateChatOnScreen();
            _ = HandleRequestedCallAsync();
        };
    }

    // For a while after leaving the screen the connection is kept, and the server, seeing the user
    // connected, sends no push: what comes then is notified about from here. On the screen the message
    // makes the sound of the application instead, also in the chat that is open; the notification has
    // the sound of the system
    private void OnChatMessageReceived(PushPayload message)
    {
        if (MainActivity.IsInFront)
            _services.GetService<ISoundService>()?.Play(message.Mention ? SoundEvent.Mention : SoundEvent.Message);
        else
            ChatNotifications.Show(global::Android.App.Application.Context, message,
                _services.GetService<IMediaCache>());
    }

    // A chat read on this device needs its notification no more. Raised on any thread
    private void OnChatReadStateChanged(ChatReadState state)
    {
        if (state.UnreadCount == 0 && _viewModel.ConnectedCoordinatorId is { } coordinatorId)
            ChatNotifications.Clear(global::Android.App.Application.Context, coordinatorId, state.ChatId);
    }

    // Off the screen the connection is closed after a while, so that the server notifies the device instead
    private void OnIsInFrontChanged(bool isInFront)
    {
        _ = isInFront ? _viewModel.ResumeConnection() : _viewModel.SuspendConnection();
        _viewModel.IsWindowActive = isInFront;
        _viewModel.RefreshCallRinging();
        if (isInFront && _viewModel.ConnectedCoordinatorId is { } coordinatorId)
            IncomingCallNotifications.Clear(global::Android.App.Application.Context, coordinatorId);
        UpdateChatOnScreen();
    }

    /// <param name="isListFresh">The list has just changed, so a chat it lacks is not coming.</param>
    private void ShowRequestedChat(bool isListFresh)
    {
        if (MainActivity.RequestedChat is not { } chat)
            return;

        if (_viewModel.TryShowChat(chat.CoordinatorInstanceId, chat.ChatId))
        {
            MainActivity.RequestedChat = null;
            return;
        }

        // Another server, or the chat was deleted: the request must not fire on some later connect
        if (isListFresh && _viewModel.SessionsList is { HasSessions: true, IsStale: false })
            MainActivity.RequestedChat = null;
    }

    private async Task HandleRequestedCallAsync()
    {
        if (!_readyForCallActions || _handlingCallAction) return;
        _handlingCallAction = true;
        try
        {
            while (MainActivity.RequestedCall is { } call)
            {
                MainActivity.RequestedCall = null;
                await _viewModel.HandlePushCallAsync(call.CoordinatorId, call.UserId, call.ChatId, call.CallId,
                    call.Action == "accept");
            }
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "Handling an incoming call notification failed");
        }
        finally { _handlingCallAction = false; }
    }

    // What the user is looking at needs no notification, and the one it already has goes away
    private void UpdateChatOnScreen()
    {
        if (MainActivity.IsInFront && _viewModel is { CurrentContent: ActiveChatSessionVM chat, ConnectedCoordinatorId: { } coordinatorId })
        {
            PushMessageHandler.ChatOnScreen = (coordinatorId, chat.Id);
            ChatNotifications.Clear(global::Android.App.Application.Context, coordinatorId, chat.Id);
            return;
        }

        PushMessageHandler.ChatOnScreen = null;
    }
}
