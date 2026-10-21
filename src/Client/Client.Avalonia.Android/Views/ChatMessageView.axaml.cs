using System;
using Avalonia.Controls;
using Client.Avalonia.Styles.Animation;
using Client.ViewModels.Sessions.Chat;

namespace Client.Droid.Views;

public partial class ChatMessageView : UserControl
{
    public ChatMessageView()
    {
        InitializeComponent();
    }

    // Views are recycled while scrolling, so the flag lives in the view model and is cleared on the first play
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ChatMessageVM { IsNew: true } vm)
        {
            vm.IsNew = false;
            // A new message fades in while rising into its place from slightly below
            Presence.Play(this, AppearEffect.Rise);
        }
    }
}
