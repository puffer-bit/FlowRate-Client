using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// A resolution a video may be compressed to, as an item of the menu of its card.
/// </summary>
public sealed class ChatVideoSideOptionVM(int side) : ReactiveObject
{
    public int Side { get; } = side;

    public string Label { get; } = $"{side}p";

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}
