using Avalonia;
using Client.Views.Common;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Views.Dialogs;
using Client.ViewModels.Sessions.List;

namespace Client.Views.Sessions.List;

public partial class SessionListItemHybridView : ViewControl<HybridSessionVM>
{
    public SessionListItemHybridView()
    {
        InitializeComponent();
        
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
        });
    }

    // One click joins, as in Discord; a click on the session we are in brings it back on screen
    private void OnHeaderTapped(object? sender, TappedEventArgs e)
    {
        ViewModel?.Connect();
        e.Handled = true;
    }
}