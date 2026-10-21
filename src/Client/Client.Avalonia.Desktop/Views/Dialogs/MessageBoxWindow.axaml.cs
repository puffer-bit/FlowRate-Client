using System;
using Client.Views.Common;
using System.Reactive.Linq;
using ReactiveUI;
using Client.ViewModels.Dialogs;

namespace Client.Views.Dialogs;

internal partial class MessageBoxWindow : ViewWindow<MessageBoxVM>
{
    public MessageBoxWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // Any of the buttons closes the window with its result
            disposables.Add(ViewModel!.OkCommand
                .Merge(ViewModel.CancelCommand)
                .Merge(ViewModel.NoCommand)
                .Subscribe(result => Close((object?)result)));
        });
    }
}
