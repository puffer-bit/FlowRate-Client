using ReactiveUI;

namespace Client.ViewModels.Sessions.Create;

public sealed class ChatMemberChoiceVM(int id, string name) : ReactiveObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public bool IsSelected { get; set { this.RaiseAndSetIfChanged(ref field, value); } }
}
