using System.Reactive;
using ReactiveUI;

namespace Client.ViewModels.Dialogs;

public class ButtonVM : ReactiveObject
{
    public string Text { get; set; }
    public ReactiveCommand<Unit, Result> Command { get; set; }

    /// <summary>Pressed by Enter: the answer the message box asks for.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Pressed by Escape: the answer that backs out.</summary>
    public bool IsCancel { get; init; }

    public ButtonVM(string text, ReactiveCommand<Unit, Result> command)
    {
        Text = text;
        Command = command;
    }
}
