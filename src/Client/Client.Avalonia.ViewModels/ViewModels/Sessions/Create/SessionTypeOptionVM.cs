using ReactiveUI;
using Shared.Enums;

namespace Client.ViewModels.Sessions.Create;

/// <summary>
/// A session type the dialog offers, shown as a selectable card.
/// </summary>
public sealed class SessionTypeOptionVM : ReactiveObject
{
    public SessionType Type { get; }
    public string Title { get; }
    public string Description { get; }
    /// <summary>Null for chats, which have no capacity.</summary>
    public int? DefaultCapacity { get; }

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public SessionTypeOptionVM(SessionType type, string title, string description, int? defaultCapacity = null)
    {
        Type = type;
        Title = title;
        Description = description;
        DefaultCapacity = defaultCapacity;
    }
}
