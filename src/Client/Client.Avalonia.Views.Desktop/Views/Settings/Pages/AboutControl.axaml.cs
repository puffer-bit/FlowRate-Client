using Avalonia.Controls;
using Avalonia;
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ViewModel is { } viewModel)
            _ = viewModel.ReleaseHistory.EnsureLoadedAsync();
    }

    private void OnReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: System.Uri url })
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(url);
    }

    // Opened by the system, in the browser of the user
    private void OnRepositoryClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(viewModel.RepositoryUrl);
    }
}
