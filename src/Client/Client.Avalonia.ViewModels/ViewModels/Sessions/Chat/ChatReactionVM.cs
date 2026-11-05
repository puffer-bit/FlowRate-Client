using System;
using System.Threading.Tasks;

namespace Client.ViewModels.Sessions.Chat;

public sealed class ChatReactionVM(string emoji, int count, bool isMine, Func<string, bool, Task> react)
{
    public string Emoji { get; } = emoji;
    public int Count { get; } = count;
    public bool IsMine { get; } = isMine;
    public bool HasCount => Count > 0;

    public void Toggle() => _ = react(Emoji, false);
    public void Choose() => _ = react(Emoji, true);
}
