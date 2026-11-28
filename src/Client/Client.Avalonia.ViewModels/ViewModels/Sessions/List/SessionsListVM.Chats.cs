using System;
using System.Collections.Generic;
using System.Globalization;
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
using Client.ViewModels.Sessions.Chat;
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

    private void AddChatSession(ChatSessionModel chatSessionModel, bool isMuted, DateTimeOffset? mutedUntil = null)
    {
        var model = new ChatSessionVM(
            chatSessionModel,
            _showSessionCommand,
            _deleteSessionCommand,
            _toggleChatMuteCommand,
            _editPermissionsCommand,
            _leaveChatCommand)
        {
            EditSessionCommand = _editSessionCommand
        };
        if (CoordinatorInstanceId is { } coordinatorInstanceId)
            model.Draft = Drafts?.Get(coordinatorInstanceId, chatSessionModel.Id);
        if (_readTracker.Get(chatSessionModel.Id) is { } readState)
            model.ApplyReadState(readState);
        model.ApplyAccess(Access.Permissions);
        model.EditGroupCommand = ReactiveCommand.CreateFromTask(() => EditGroup(model));
        model.LeaveGroupCommand = ReactiveCommand.CreateFromTask(() => LeaveGroup(model));
        model.CallCommand = ReactiveCommand.CreateFromTask(() => CallAsync(model.Model.Id));
        model.MuteForCommand = ReactiveCommand.CreateFromTask<string?>(minutes => MuteChatFor(model, minutes));
        if (isMuted)
            ApplyChatMute(model, mutedUntil);

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
        {
            _ = _preferences.SaveChatMuteAsync(coordinatorInstanceId, sessionId, false);
            Drafts?.Remove(coordinatorInstanceId, sessionId);
        }

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
        chat.PeerIsMobile = other.IsMobile;
    }

    // Unmutes a muted chat, mutes another one until the user turns its notifications on
    private async Task ToggleChatMute(int chatId)
    {
        if (CoordinatorInstanceId is not { } coordinatorInstanceId || FindSession<ChatSessionVM>(chatId) is not { } chat)
            return;

        var isMuted = !chat.IsMuted;
        if (isMuted)
            ApplyChatMute(chat, null);
        else
            EndChatMute(chat);
        RefreshCategoryCounts();
        await _preferences.SaveChatMuteAsync(coordinatorInstanceId, chatId, isMuted);
    }

    /// <param name="minutes">How long the chat stays muted; empty for until the user turns the notifications on.</param>
    private async Task MuteChatFor(ChatSessionVM chat, string? minutes)
    {
        if (CoordinatorInstanceId is not { } coordinatorInstanceId)
            return;

        DateTimeOffset? until = int.TryParse(minutes, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0
            ? DateTimeOffset.UtcNow.AddMinutes(count)
            : null;
        ApplyChatMute(chat, until);
        RefreshCategoryCounts();
        await _preferences.SaveChatMuteAsync(coordinatorInstanceId, chat.Model.Id, true, until);
    }

    // A mute for a while ends by itself; what is saved is past its time then and is not read again
    private void ApplyChatMute(ChatSessionVM chat, DateTimeOffset? until)
    {
        chat.MuteEnd?.Dispose();
        chat.MuteEnd = null;
        chat.IsMuted = true;
        chat.MutedUntil = until;
        if (until is not { } end)
            return;

        // A timer's interval is limited; a longer mute is looked at again when this one fires
        var delay = end - DateTimeOffset.UtcNow;
        var step = delay < MaxMuteTimerStep ? delay : MaxMuteTimerStep;
        if (step < TimeSpan.Zero)
            step = TimeSpan.Zero;
        chat.MuteEnd = DispatcherTimer.RunOnce(() =>
        {
            if (chat.MutedUntil != end)
                return;
            if (DateTimeOffset.UtcNow < end)
            {
                ApplyChatMute(chat, end);
                return;
            }

            EndChatMute(chat);
            RefreshCategoryCounts();
        }, step);
    }

    private static readonly TimeSpan MaxMuteTimerStep = TimeSpan.FromDays(1);

    private static void EndChatMute(ChatSessionVM chat)
    {
        chat.MuteEnd?.Dispose();
        chat.MuteEnd = null;
        chat.IsMuted = false;
        chat.MutedUntil = null;
    }

    /// <summary>The texts typed in chats and not sent, which the chats of the list show.</summary>
    public ChatDrafts? Drafts
    {
        get;
        set
        {
            if (field != null)
                field.Changed -= OnDraftChanged;
            field = value;
            if (value != null)
                value.Changed += OnDraftChanged;
        }
    }

    private void OnDraftChanged(string coordinatorInstanceId, int chatId, string? text)
    {
        if (coordinatorInstanceId == CoordinatorInstanceId && FindSession<ChatSessionVM>(chatId) is { } chat)
            chat.Draft = text;
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
