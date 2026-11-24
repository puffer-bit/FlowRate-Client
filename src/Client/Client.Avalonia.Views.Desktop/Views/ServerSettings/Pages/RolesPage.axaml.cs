using Avalonia.Controls;

namespace Client.Views.ServerSettings.Pages;

public partial class RolesPage : UserControl
{
    private bool? _isWide;

    public RolesPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= 720;
            if (_isWide == wide)
                return;
            _isWide = wide;
            // Keep the definitions in place while layout is running; only their widths change.
            RolesLayout.ColumnDefinitions[0].Width = wide ? new GridLength(230) : new GridLength(1, GridUnitType.Star);
            RolesLayout.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(RoleEditor, wide ? 1 : 0);
            Grid.SetRow(RoleEditor, wide ? 0 : 1);
        };
    }
}
