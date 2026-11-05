using Avalonia.Interactivity;
using Avalonia.Threading;
using Client.Views.Dialogs;
using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid;

public partial class HybridSourceSelectView : ViewControl<VideoSourceSelectWindowVM>
{
    public HybridSourceSelectView(VideoSourceSelectWindowVM viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.LoadSources();
    }

    public HybridSourceSelectView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
        });
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Run after the overlay's automatic focus so opening the dialog starts at the source.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsLoaded)
                return;

            if (ViewModel?.UsesSystemPicker == true)
                SystemSourceButton.Focus();
            else
                WindowsSourceButton.Focus();

            SettingsScroll.ScrollToHome();
        }, DispatcherPriority.Input);
    }

}
