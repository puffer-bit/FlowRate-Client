using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Tools.AppInfo;

namespace Client.Views.Main;

/// <summary>
/// The bar over the content of the main window with what the menu had.
/// </summary>
internal partial class MainTopBar : UserControl
{
    public MainTopBar()
    {
        InitializeComponent();
    }

    private void OnOpenRepository(object? sender, RoutedEventArgs e)
    {
        _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(AppInformation.RepositoryUrl));
    }
}
