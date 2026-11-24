using Client.Views.Dialogs;
using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Profile;

namespace Client.Views.Profile;

internal partial class AccountSecurityView : ViewControl<AccountSecurityVM>
{
    private async void CopyPhrase(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel?.RecoveryPhrase is { } phrase)
            await ClipboardAccess.SetTextAsync(this, phrase);
    }

    public AccountSecurityView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.TogglePhraseVisibility = true;
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
        });
    }
}
