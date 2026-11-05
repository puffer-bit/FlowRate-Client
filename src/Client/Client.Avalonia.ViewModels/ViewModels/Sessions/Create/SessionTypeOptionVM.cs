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
    public int DefaultCapacity { get; }

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public SessionTypeOptionVM(SessionType type, string title, string description, int defaultCapacity)
    {
        Type = type;
        Title = title;
        Description = description;
        DefaultCapacity = defaultCapacity;
    }
}
