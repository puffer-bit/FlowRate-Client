using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Client.Services;
using Client.Views.Common;
using Avalonia.VisualTree;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.List;

namespace Client.Views.Sessions.List;

public partial class SessionListCategoryView : ViewControl<SessionCategoryVM>
{
    public SessionListCategoryView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // The name opens for typing with all of it selected, so a new category is named by just typing
            ViewModel!.WatchProperty(nameof(SessionCategoryVM.IsEditing), vm => vm.IsEditing)
                .Subscribe(isEditing =>
                {
                    if (!isEditing)
                        return;

                    Dispatcher.UIThread.Post(() =>
                    {
                        NameEditor.Focus();
                        NameEditor.SelectAll();
                    }, DispatcherPriority.Input);
                })
                .DisposeWith(disposables);
        });
    }

    private void OnHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { IsEditing: false } category)
            return;

        // A tap on the add button is the button's own
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) != null)
            return;

        category.ToggleCollapseCommand.Execute().Subscribe();
        e.Handled = true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } category)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                category.CommitRenameCommand.Execute().Subscribe();
                e.Handled = true;
                break;
            case Key.Escape:
                category.CancelRenameCommand.Execute().Subscribe();
                e.Handled = true;
                break;
        }
    }

    // Clicking elsewhere keeps what was typed, as Enter does
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) =>
        ViewModel?.CommitRenameCommand.Execute().Subscribe();
}
