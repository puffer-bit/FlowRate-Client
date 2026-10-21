using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;

namespace Client.Views.Settings.Pages;

public partial class AboutControl : ViewControl<AboutControlVM>
{
    public AboutControl()
    {
        InitializeComponent();
    }

    // Opened by the system, in the browser of the user
    private void OnRepositoryClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(viewModel.RepositoryUrl);
    }
}
