using Client.Avalonia.Localization;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Client.Services;
using ReactiveUI;
using MessageBoxIcon = Client.ViewModels.Dialogs.Icon;

namespace Client.ViewModels.Dialogs;

public class MessageBoxVM : ReactiveObject
{
    public ReactiveCommand<Unit, Result> CancelCommand { get; }
    public ReactiveCommand<Unit, Result> OkCommand { get; }
    public ReactiveCommand<Unit, Result> NoCommand { get; }

    /// <summary>The answer of whichever button is pressed.</summary>
    public IObservable<Result> Answers { get; }
    
    public ObservableCollection<ButtonVM> ButtonsList { get; } = new();

    /// <summary>Key of a DrawingImage resource from Client.Avalonia.Assets/IconImages.axaml.</summary>
    public string IconKey { get; }
    public Buttons Buttons { get; set; }
    public string Title { get; set; }
    public string Text { get; set; }

    /// <summary>
    /// What the text is about, such as an address, shown under it on a background of its own.
    /// </summary>
    public string? Detail { get; init; }
    public bool HasDetail => Detail != null;

    /// <summary>
    /// The label of a check box under the text; without it the box has none.
    /// </summary>
    public string? CheckBoxText { get; init; }
    public bool HasCheckBox => CheckBoxText != null;

    /// <summary>
    /// Whether the check box was checked, read after the box is answered.
    /// </summary>
    public bool IsChecked { get; set; }

    public MessageBoxVM(MessageBoxIcon icon, Buttons buttons, string text, string title)
    {
        CancelCommand = ReactiveCommand.Create<Unit, Result>(_ => Result.Cancel);
        
        OkCommand = ReactiveCommand.Create<Unit, Result>(_ =>
            Buttons is Buttons.YesNo ? Result.Yes : Result.Ok);
        
        NoCommand = ReactiveCommand.Create<Unit, Result>(_ => Result.No);

        Answers = OkCommand.Merge(CancelCommand).Merge(NoCommand);
        
        IconKey = IconKeys.Of(icon switch
        {
            MessageBoxIcon.Info => IconType.Info,
            MessageBoxIcon.Question => IconType.Question,
            MessageBoxIcon.Warning => IconType.Warning,
            MessageBoxIcon.KeyWarning => IconType.KeyWarning,
            MessageBoxIcon.KeyQuestion => IconType.KeyQuestion,
            MessageBoxIcon.Error => IconType.Error,
            MessageBoxIcon.Fatal => IconType.Stop,
            MessageBoxIcon.Loading or MessageBoxIcon.None => throw new NotImplementedException(),
            _ => IconType.Info
        });
        Buttons = buttons;
        Text = text;
        Title = title;
        LoadMessageBox();
    }

    private void LoadMessageBox()
    {
        ButtonsList.Clear();

        switch (Buttons)
        {
            case Buttons.OkCancel:
                ButtonsList.Add(new ButtonVM(Strings.Get("common.ok"), OkCommand) { IsDefault = true });
                ButtonsList.Add(new ButtonVM(Strings.Get("common.cancel"), CancelCommand) { IsCancel = true });
                break;

            case Buttons.Ok:
                ButtonsList.Add(new ButtonVM(Strings.Get("common.ok"), OkCommand) { IsDefault = true });
                break;

            case Buttons.YesNo:
                ButtonsList.Add(new ButtonVM(Strings.Get("common.yes"), OkCommand) { IsDefault = true });
                ButtonsList.Add(new ButtonVM(Strings.Get("common.no"), NoCommand) { IsCancel = true });
                break;
        }
    }
}
