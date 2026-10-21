using Avalonia.Controls;
using Client.ViewModels.Connections;

namespace Client.Droid.Views;

public partial class ConnectView : UserControl
{
    public ConnectView()
    {
        InitializeComponent();

        // Once the user is done with the address, a port typed into it moves to the port field
        AddressInput.LostFocus += (_, _) => (DataContext as ConnectWindowVM)?.NormalizeAddress();
    }
}
