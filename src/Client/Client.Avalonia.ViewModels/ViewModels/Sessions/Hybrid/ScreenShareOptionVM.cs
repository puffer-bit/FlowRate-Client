using System;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// One of the values an option of a screen share is chosen from.
/// </summary>
public class ScreenShareOptionVM : ReactiveObject
{
    public int Value { get; }

    public string Label { get; }

    /// <summary>
    /// The option asks a lot of the hardware and the network, and is marked so.
    /// </summary>
    public bool IsDemanding { get; init; }

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> SelectCommand { get; }

    public ScreenShareOptionVM(int value, string label, Action<ScreenShareOptionVM> select)
    {
        Value = value;
        Label = label;
        SelectCommand = ReactiveCommand.Create(() => select(this));
    }
}
