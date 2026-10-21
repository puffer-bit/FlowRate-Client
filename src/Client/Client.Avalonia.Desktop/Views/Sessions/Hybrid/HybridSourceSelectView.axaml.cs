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
}
