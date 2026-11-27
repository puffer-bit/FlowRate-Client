using System;
using Shared.Enums.User;
using ReactiveUI;
using System.Threading.Tasks;

namespace Client.ViewModels.Sessions.Chat;

/// <param name="details">Who set the reaction and when, a line each; shown to everyone.</param>
public sealed class ChatReactionVM(string emoji, int count, bool isMine, Func<string, bool, Task> react,
    string? details = null) : ReactiveUI.ReactiveObject
{
    public string Emoji { get; } = emoji;
    public int Count { get; private set; } = count;
    public bool IsMine { get; private set; } = isMine;
    public bool HasCount => Count > 0;
    public string? Details { get; private set; } = details;

    public void Update(int count, bool isMine, string? details)
    {
        Count = count;
        IsMine = isMine;
        Details = details;
        this.RaisePropertyChanged(nameof(Count));
        this.RaisePropertyChanged(nameof(HasCount));
        this.RaisePropertyChanged(nameof(IsMine));
        this.RaisePropertyChanged(nameof(Details));
    }

    public void Toggle() => _ = react(Emoji, false);
    public void Choose() => _ = react(Emoji, true);
}

/// <summary>One reaction of one user, for the list of who reacted.</summary>
public sealed record ChatReactionDetailVM(string Emoji, string Name, string TimeText, int UserId = 0,
    string? AvatarUrl = null, UserStatus? Status = null, bool IsMobile = false);
