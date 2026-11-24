using Avalonia.Interactivity;
using Client.Avalonia.Controls.Menus;

namespace Client.Views.Common;

public class HoverActionMenuItem : MenuItemButton
{
    protected override void OnClick()
    {
        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }
}
