using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Main;

namespace Client.Droid.Views.Main;

/// <summary>
/// The voice session we are in, as a bar at the bottom of the home screen.
/// </summary>
public partial class CallBar : UserControl
{
    public CallBar()
    {
        InitializeComponent();
    }

    // Brings the session on screen
    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowVM viewModel)
            viewModel.ShowHybridSessionCommand.Execute().Subscribe();
    }
}
