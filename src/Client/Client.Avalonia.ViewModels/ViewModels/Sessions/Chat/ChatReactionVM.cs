using System;
using System.Threading.Tasks;

namespace Client.ViewModels.Sessions.Chat;

/// <param name="details">Who set the reaction and when, a line each; shown to everyone.</param>
public sealed class ChatReactionVM(string emoji, int count, bool isMine, Func<string, bool, Task> react,
    string? details = null)
{
    public string Emoji { get; } = emoji;
    public int Count { get; } = count;
    public bool IsMine { get; } = isMine;
    public bool HasCount => Count > 0;
    public string? Details { get; } = details;

    public void Toggle() => _ = react(Emoji, false);
    public void Choose() => _ = react(Emoji, true);
}

/// <summary>One reaction of one user, for the list of who reacted.</summary>
public sealed record ChatReactionDetailVM(string Emoji, string Name, string TimeText);
