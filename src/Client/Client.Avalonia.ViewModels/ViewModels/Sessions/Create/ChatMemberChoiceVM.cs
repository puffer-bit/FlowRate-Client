using ReactiveUI;

namespace Client.ViewModels.Sessions.Create;

public sealed class ChatMemberChoiceVM(int id, string name) : ReactiveObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string? Username { get; init; }
    public string? AvatarUrl { get; init; }
    public bool HasUsername => !string.IsNullOrEmpty(Username) && Username != Name;
    public bool CanToggle { get; set { this.RaiseAndSetIfChanged(ref field, value); } } = true;
    public bool IsSelected { get; set { this.RaiseAndSetIfChanged(ref field, value); } }
}
