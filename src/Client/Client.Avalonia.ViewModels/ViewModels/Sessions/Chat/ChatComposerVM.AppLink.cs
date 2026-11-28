using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The card of the <c>flowrate://</c> link being typed, shown above the input as the message will show it. It is made
/// on the client and asks the server nothing; it has no button there.
/// </summary>
public sealed partial class ChatComposerVM
{
    public ChatAppLinkVM? ComposerAppLink
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsAppLinkBarVisible));
        }
    }

    public bool IsAppLinkBarVisible => CanWrite && ComposerAppLink != null;

    private void OnAppLinkInputChanged()
    {
        var card = ChatAppLinkVM.FromText(InputtedText, canOpen: false);
        // The same link keeps its card while the rest of the text is typed
        if (card?.Address != ComposerAppLink?.Address)
            ComposerAppLink = card;
    }
}
