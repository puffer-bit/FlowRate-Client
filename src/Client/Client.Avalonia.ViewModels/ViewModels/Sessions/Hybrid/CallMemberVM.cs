using System;
using Client.Avalonia.Localization;
using Client.Core.Users;
using ReactiveUI;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>A member of the chat who is not in its call: rung, declined, missed, gone or never rung.</summary>
public sealed class CallMemberVM : ReactiveObject
{
    private readonly Action<CallMemberVM> _ring;
    private readonly Action<CallMemberVM> _removeFromGroup;

    public CallMemberVM(int userId, Action<CallMemberVM> ring, Action<CallMemberVM> removeFromGroup)
    {
        UserId = userId;
        _ring = ring;
        _removeFromGroup = removeFromGroup;
    }

    public int UserId { get; }

    public UserModel? UserData
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(DisplayedName));
        }
    }

    public string DisplayedName => UserData?.DisplayName ?? UserData?.Username ?? Strings.Get("calls.peer");

    /// <summary>Where the member stands in the call; null when they were never rung.</summary>
    public ChatCallParticipantState? State
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsRinging));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public bool IsRinging => State == ChatCallParticipantState.Ringing;

    public string StatusText => Strings.Get(State switch
    {
        ChatCallParticipantState.Ringing => "calls.member.ringing",
        ChatCallParticipantState.Declined => "calls.member.declined",
        ChatCallParticipantState.Missed => "calls.member.missed",
        ChatCallParticipantState.Left => "calls.member.left",
        _ => "calls.member.notInCall"
    });

    /// <summary>The owner of the group; a crown stands before the name.</summary>
    public bool IsOwner
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The current user owns the group and may take this member out of it.</summary>
    public bool CanRemoveFromGroup
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Calls the member again; while it already rings there is nothing to do.</summary>
    public void Ring()
    {
        if (!IsRinging)
            _ring(this);
    }

    public void RemoveFromGroup()
    {
        if (CanRemoveFromGroup)
            _removeFromGroup(this);
    }
}
