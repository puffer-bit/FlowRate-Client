using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Sessions.Create;
using ReactiveUI;
using Shared.Enums;
using Shared.Models.DataTransferObjects;
using Shared.Validation;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

/// <summary>
/// The chats of the list: adding and updating them, the title of a direct chat, muting, groups and unread counts.
/// </summary>
public partial class SessionsListVM
{
    // A chat created just now is not muted
    private void AddNewChatSession(ChatSessionModel chatSessionModel) => Dispatcher.UIThread.Post(() =>
    {
        if (FindSession<ChatSessionVM>(chatSessionModel.Id) == null) AddChatSession(chatSessionModel, false);
    });

    private void AddChatSession(ChatSessionModel chatSessionModel, bool isMuted)
    {
        var model = new ChatSessionVM(
            chatSessionModel,
            _showSessionCommand,
            _deleteSessionCommand,
            _toggleChatMuteCommand,
            _editPermissionsCommand,
            _leaveChatCommand)
        {
            IsMuted = isMuted,
            EditSessionCommand = _editSessionCommand
        };
        if (_readTracker.Get(chatSessionModel.Id) is { } readState)
            model.ApplyReadState(readState);
        model.ApplyAccess(Access.Permissions);
        model.EditGroupCommand = ReactiveCommand.CreateFromTask(() => EditGroup(model));
        model.LeaveGroupCommand = ReactiveCommand.CreateFromTask(() => LeaveGroup(model));
        model.CallCommand = ReactiveCommand.CreateFromTask(() => CallAsync(model.Model.Id));

        MarkShown(model);
        if (_coordinatorSession?.ChatCalls.GetValueOrDefault(chatSessionModel.Id) is { } running)
        {
            model.HasActiveCall = true;
            FollowCall(model, running.State == ChatCallState.Active ? running.CallId : null, null);
        }
        model.MoveTargets = MoveTargets;
        Sessions.Add(model);
        _ = UpdatePrivateTitleAsync(model);
        RebuildRows();
        RaiseStateProperties();
    }

    // Raised on the socket thread
    private void OnChatSessionReconfigured(ChatSessionModel model)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Sessions.OfType<ChatSessionVM>().FirstOrDefault(s => s.Model.Id == model.Id && !s.IsLeaving) is not
                { } session)
                return;

            session.Model = model;
            _ = UpdatePrivateTitleAsync(session);
            RebuildRows();
            RaiseStateProperties();
            // Its overrides may have changed
            session.ApplyAccess(Access.Permissions);
            // The owner of a group kicks from its call
            if (session.CallPeers is { Model: HybridSessionModel call } callPeers)
            {
                call.CallOwnerUserId = model.Kind == ChatKind.Group ? model.OwnerUserId : null;
                callPeers.ApplyAccess(Access.Permissions);
            }
        });
    }

    private void RemoveChatSession(int sessionId)
    {
        if (CoordinatorInstanceId is { } coordinatorInstanceId)
            _ = _preferences.SaveChatMuteAsync(coordinatorInstanceId, sessionId, false);

        RemoveSession(sessionId);
    }

    private async Task UpdatePrivateTitleAsync(ChatSessionVM chat)
    {
        if (chat.Model is not ChatSessionModel { Kind: ChatKind.Direct } model || _coordinatorSession is not { } coordinator) return;
        var otherId = model.MemberIds.FirstOrDefault(id => id != coordinator.GetCurrentUser().Id);
        var other = await coordinator.GetUser(otherId);
        if (other != null)
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(chat.Model, model))
                    ApplyDirectPeer(chat, other);
            });
    }

    /// <summary>A direct chat is shown as the other member: their name, avatar and status.</summary>
    private static void ApplyDirectPeer(ChatSessionVM chat, UserModel other)
    {
        chat.DisplayName = other.DisplayName ?? other.Username;
        chat.AvatarUrl = other.AvatarUrl;
        chat.PeerStatus = other.Status;
    }

    private async Task ToggleChatMute(int chatId)
    {
        if (CoordinatorInstanceId is not { } coordinatorInstanceId || FindSession<ChatSessionVM>(chatId) is not { } chat)
            return;

        chat.IsMuted = !chat.IsMuted;
        RefreshCategoryCounts();
        await _preferences.SaveChatMuteAsync(coordinatorInstanceId, chatId, chat.IsMuted);
    }

    public Interaction<CreateSessionWindowVM, RxVoid>? ShowGroupEditor { get; set; }

    /// <summary>Prepares the picture the owner picks for a group in its editor.</summary>
    public Client.Services.IAvatarService? Avatars { get; set; }

    private async Task EditGroup(ChatSessionVM chat)
    {
        if (_coordinatorSession == null || ShowGroupEditor == null || chat.Model is not ChatSessionModel model) return;
        using var editor = new CreateSessionWindowVM(_coordinatorSession,
            privateMode: true, editingGroup: model, avatars: Avatars);
        await ShowGroupEditor.Handle(editor);
    }

    private async Task LeaveGroup(ChatSessionVM chat)
    {
        if (_coordinatorSession == null || chat.Model is not ChatSessionModel model) return;
        var result = await _coordinatorSession.LeaveGroup(model.Id);
        if (!result.IsSuccess && !result.IsRejected)
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
                Strings.Get("privateChats.leaveFailed"), Strings.Get("common.error")));
    }

    /// <summary>The owner takes a member out of the group, after being asked.</summary>
    public async Task RemoveFromGroupAsync(int chatId, int userId, string name)
    {
        if (_coordinatorSession == null
            || FindSession<ChatSessionVM>(chatId)?.Model is not ChatSessionModel { Kind: ChatKind.Group } model
            || model.OwnerUserId is not { } ownerId || userId == ownerId) return;
        var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
            Strings.Format("privateChats.removeMemberConfirm", ("user", name)), Strings.Get("privateChats.removeMember")));
        if (answer != Result.Yes) return;
        var result = await _coordinatorSession.ChangeGroup(model.Id, model.Name,
            model.MemberIds.Where(id => id != userId).ToArray(), ownerId);
        if (!result.IsSuccess && !result.IsRejected)
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
                Strings.Get("privateChats.removeMemberFailed"), Strings.Get("common.error")));
    }

    private async Task LoadReadStates(ICoordinatorSession coordinatorSession, int attachId)
    {
        if (attachId != _attachId || State != SessionsListState.Loaded)
            return;

        foreach (var chat in Sessions.OfType<ChatSessionVM>().ToArray())
        {
            var position = _readTracker.Get(chat.Model.Id)?.LastReadMessageId ?? 0;
            int? before = null;
            while (true)
            {
                var (result, page) = await coordinatorSession.RequestChatHistoryPage(chat.Model.Id, before,
                    Limits.ChatHistoryMaxTake);
                if (attachId != _attachId)
                    return;
                if (!result.IsSuccess || page == null)
                {
                    LogReadStatesLoadFailed(result);
                    break;
                }
                if (page.Messages.Count == 0)
                    break;
                foreach (var message in page.Messages.OrderBy(message => message.Id))
                    _readTracker.OnHistoryMessage(chat.Model.Id, message);
                var oldest = page.Messages.Min(message => message.Id);
                if (oldest <= position || page.Messages.Any(message => !message.IsSystem &&
                        message.SenderUserId == coordinatorSession.GetCurrentUser().Id) ||
                    (before.HasValue && oldest >= before.Value))
                    break;
                before = oldest;
            }
            if (_readTracker.Get(chat.Model.Id) is { } state)
                chat.ApplyReadState(state);
        }
        RefreshCategoryCounts();
    }

    // Raised on the socket thread or the UI thread
    private void OnReadStateChanged(ChatReadState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (FindSession<ChatSessionVM>(state.ChatId) is { } chat)
            {
                chat.ApplyReadState(state);
                RefreshCategoryCounts();
            }
        });
    }
}
