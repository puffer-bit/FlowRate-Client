using ReactiveUI;
using Shared.Enums.User;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// One line of the mention list: a user, or everyone / those online.
/// </summary>
public class ChatMentionSuggestionVM : ReactiveObject
{
    /// <summary>The name written after "@".</summary>
    public required string Name { get; init; }
    public int? UserId { get; init; }
    public string? AvatarUrl { get; init; }
    public UserStatus? Status { get; init; }
    public string? Hint { get; init; }
    public bool HasHint => Hint != null;
    public bool IsGroup => UserId == null;

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}
