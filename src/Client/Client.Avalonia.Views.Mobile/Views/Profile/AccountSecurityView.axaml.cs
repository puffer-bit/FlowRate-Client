using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Client.ViewModels.Profile;

namespace Client.Mobile.Views.Profile;

/// <summary>
/// What the user logs in with, on the whole screen: the password, one-time codes and passkeys.
/// </summary>
public partial class AccountSecurityView : UserControl
{
    public AccountSecurityView()
    {
        InitializeComponent();
    }

    // The kept phrase is shown and hidden by one button, as on the desktop
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is AccountSecurityVM security)
            security.TogglePhraseVisibility = true;
    }

    private async void OnCopyPhraseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountSecurityVM { RecoveryPhrase: { } phrase } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(phrase);
    }
}
