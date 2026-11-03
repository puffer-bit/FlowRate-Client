using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Client.Droid.Views;

public partial class HybridSessionStatsPage : UserControl
{
    public event Action? CloseRequested;

    public HybridSessionStatsPage()
    {
        InitializeComponent();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
