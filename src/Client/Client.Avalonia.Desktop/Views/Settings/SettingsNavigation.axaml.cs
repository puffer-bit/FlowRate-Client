using System;
using Client.Views.Common;
using Avalonia.Input;
using Client.ViewModels.Settings;

namespace Client.Views.Settings;

/// <summary>
/// The side column of the settings: the search and, under it, the sections or the settings that match.
/// </summary>
internal partial class SettingsNavigation : ViewControl<SettingsWindowVM>
{
    public SettingsNavigation()
    {
        InitializeComponent();
    }

    // Enter opens the best match, Down goes into the results, Escape drops the search
    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                vm.ShowFirstSearchResult();
                e.Handled = true;
                break;

            case Key.Down when vm.SearchResults.Count > 0:
                if (SearchResultsList.SelectedItem == null)
                    vm.ShowFirstSearchResult();
                SearchResultsList.ContainerFromIndex(Math.Max(SearchResultsList.SelectedIndex, 0))?.Focus();
                e.Handled = true;
                break;

            case Key.Escape when vm.IsSearching:
                vm.SearchQuery = null;
                e.Handled = true;
                break;
        }
    }
}
