using ReactiveUI;
using Shared.Enums.User;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// One line of the mention list: a user, or everyone / those online.
/// </summary>
public class ChatMentionSuggestionVM : ReactiveObject
{
    /// <summary>The name written after "@".</summary>
    public required string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public int? UserId { get; init; }
    public string? AvatarUrl
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public UserStatus? Status
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
    public string? Hint
    {
        get;
        set
        {
            if (field == value) return;
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasHint));
        }
    }
    public bool HasHint => Hint != null;
    public bool IsGroup => UserId == null;

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}
