using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Core.Settings;

namespace Client.Views.Dialogs;

/// <summary>What the user chose in <see cref="CloseAppDialog"/>.</summary>
internal sealed record CloseAppDialogResult(CloseButtonAction Action, bool RememberChoice);

/// <summary>
/// Asks what the close button of the main window does. Shown with <see cref="InWindowDialog"/>,
/// it returns a <see cref="CloseAppDialogResult"/>, or null when cancelled.
/// </summary>
internal partial class CloseAppDialog : UserControl
{
    public CloseAppDialog()
    {
        InitializeComponent();
    }

    private void OnMinimizeToTrayClick(object? sender, RoutedEventArgs e) => CloseWith(CloseButtonAction.MinimizeToTray);

    private void OnExitClick(object? sender, RoutedEventArgs e) => CloseWith(CloseButtonAction.Exit);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => InWindowDialog.Close(this);

    private void CloseWith(CloseButtonAction action)
    {
        InWindowDialog.Close(this, new CloseAppDialogResult(action, RememberChoiceCheckBox.IsChecked == true));
    }
}
