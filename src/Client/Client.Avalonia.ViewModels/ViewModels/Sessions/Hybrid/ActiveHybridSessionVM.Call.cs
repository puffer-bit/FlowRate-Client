using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Client.Core.Sessions;
using Client.ViewModels.Sessions.List;
using ReactiveUI;
using Shared.Enums;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.Sessions.Hybrid;

public partial class ActiveHybridSessionVM
{
    /// <summary>
    /// The peers of the session as the sessions list shows them, so the panel has the same rows and menus.
    /// </summary>
    public HybridSessionVM? PeerList
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The members of the chat who are not in its call, with a way to ring them again.</summary>
    public ObservableCollection<CallMemberVM> CallMembers { get; } = new();

    public bool HasCallMembers
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The call is running, but nobody else has joined yet.</summary>
    public bool IsWaitingForMembers
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Takes a member out of the group whose call this is: chat ID, user ID and name.</summary>
    public Func<int, int, string, Task>? RemoveFromGroup { get; set; }

    /// <summary>Shows the state of the chat call this session carries. Called on the UI thread.</summary>
    public void ApplyCall(ChatCallDTO call, ChatSessionModel? chat)
    {
        if (Model.CallId != call.CallId || IsClosed)
            return;
        var currentUserId = _coordinatorSession.GetCurrentUser().Id;
        var ownerId = chat is { Kind: ChatKind.Group } ? chat.OwnerUserId : null;
        var ownerChanged = Model.CallOwnerUserId != ownerId;
        Model.CallStartedAt = call.StartedAt;
        Model.CallOwnerUserId = ownerId;
        IsWaitingForMembers = call.StartedAt == null;
        UpdateCallDuration();
        if (ownerChanged && PeerList != null)
        {
            PeerList.Model = Model;
            PeerList.ApplyAccess(_coordinatorSession.Permissions);
        }

        var states = call.Participants.ToDictionary(p => p.UserId, p => p.State);
        var outside = (chat?.MemberIds ?? []).Concat(states.Keys).Distinct()
            .Where(id => id != currentUserId && states.GetValueOrDefault(id, (ChatCallParticipantState)(-1))
                != ChatCallParticipantState.Joined)
            .ToHashSet();
        foreach (var gone in CallMembers.Where(m => !outside.Contains(m.UserId)).ToList())
            CallMembers.Remove(gone);
        var isOwner = ownerId == currentUserId;
        foreach (var userId in outside)
        {
            var member = CallMembers.FirstOrDefault(m => m.UserId == userId);
            if (member == null)
            {
                member = new CallMemberVM(userId, RingMember, RemoveMemberFromGroup);
                CallMembers.Add(member);
                _ = LoadMemberAsync(member);
            }
            member.State = states.TryGetValue(userId, out var state) ? state : null;
            member.IsOwner = userId == ownerId;
            member.CanRemoveFromGroup = isOwner && RemoveFromGroup != null;
        }
        HasCallMembers = CallMembers.Count > 0;
    }

    private async Task LoadMemberAsync(CallMemberVM member) =>
        member.UserData = await _coordinatorSession.GetUser(member.UserId);

    private void RingMember(CallMemberVM member)
    {
        if (Model.CallId is { } callId)
            _ = _coordinatorSession.ChangeChatCall(Model.Id, callId, ChatCallAction.Ring, member.UserId);
    }

    /// <summary>Rings every member not in the call.</summary>
    public void RingEveryone()
    {
        if (Model.CallId is { } callId)
            _ = _coordinatorSession.ChangeChatCall(Model.Id, callId, ChatCallAction.Ring);
    }

    private void RemoveMemberFromGroup(CallMemberVM member) =>
        _ = RemoveFromGroup?.Invoke(Model.Id, member.UserId, member.DisplayedName);
}
