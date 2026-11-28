using System;
using ReactiveUI;

namespace Client.ViewModels.ServerSettings;

/// <summary>One color offered in the role editor; null for none.</summary>
public sealed class RoleColorVM(int? color, Action<RoleColorVM> select) : ReactiveObject
{
    /// <summary>0xRRGGBB, or null for no color.</summary>
    public int? Color { get; } = color;

    public bool IsNone => Color == null;

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public void Select() => select(this);
}
