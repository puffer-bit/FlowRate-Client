using ReactiveUI;
using Shared.Enums.User;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// One line of the input suggestions: a mention or a slash command.
/// </summary>
public class ChatMentionSuggestionVM : ReactiveObject
{
    public bool IsCommand { get; init; }
    public string? CommandArguments { get; init; }
    public string DisplayName => IsCommand
        ? "/" + Name + (CommandArguments == null ? "" : " " + CommandArguments)
        : "@" + Name;

    /// <summary>The name written after "@" or "/".</summary>
    public required string Name
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(DisplayName));
        }
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
