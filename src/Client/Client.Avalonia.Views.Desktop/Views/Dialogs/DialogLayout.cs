using Avalonia.Controls;

namespace Client.Views.Dialogs;

/// <summary>Fixed header and footer around a scrollable dialog body; rows 1 and 3 hold separators.</summary>
public class DialogLayout : Grid
{
    public DialogLayout() => RowDefinitions = RowDefinitions.Parse("Auto,1,*,1,Auto");
}
