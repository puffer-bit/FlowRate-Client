using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Client.ViewModels.Settings;

namespace Client.Views.Common;

internal static class SettingsNavigationLayout
{
    public static bool IsCollapsed(ListBox tabs) => tabs.Classes.Contains("collapsedNavigation");

    public static bool Toggle(ListBox tabs, TappedEventArgs e)
    {
        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (row == null || row.DataContext is SettingNavItemVM { IsSubItem: true } ||
            tabs.GetVisualAncestors().OfType<Grid>().FirstOrDefault(g => g.Classes.Contains("settingsFrame")) is not { } frame)
            return false;

        SetCollapsed(tabs, !IsCollapsed(tabs));
        e.Handled = true;
        return true;
    }

    public static void SetCollapsed(ListBox tabs, bool collapsed)
    {
        var frame = tabs.GetVisualAncestors().OfType<Grid>().First(g => g.Classes.Contains("settingsFrame"));
        tabs.Classes.Set("collapsedNavigation", collapsed);
        frame.Classes.Set("collapsedNavigation", collapsed);
        var navigation = frame.Children.First(c => Grid.GetColumn(c) == 0 && Grid.GetRow(c) == 2);
        navigation.Width = (collapsed ? 80 : 220) - navigation.Margin.Left - navigation.Margin.Right;
    }
}
