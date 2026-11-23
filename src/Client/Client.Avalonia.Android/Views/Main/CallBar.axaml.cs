using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;

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

    /// <summary>Asks for the reason to go away with, which the root view shows.</summary>
    public event Action<HybridSessionStatusBarVM>? AfkReasonRequested;

    private void OnAfkReasonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ActiveHybridSessionVM session })
            AfkReasonRequested?.Invoke(session.StatusBar);
    }

    // Brings the session on screen
    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowVM viewModel)
            viewModel.ShowHybridSessionCommand.Execute().Subscribe();
    }
}
