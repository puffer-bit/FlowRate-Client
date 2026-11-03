using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using Client.Views.Common;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Client.Avalonia.Styles.Animation;
using Client.ViewModels.Settings;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.Views.Settings;

/// <summary>
/// The side column of the settings: the search and, under it, the sections or the settings that match.
/// </summary>
internal partial class SettingsNavigation : ViewControl<SettingsWindowVM>
{
    public SettingsNavigation()
    {
        InitializeComponent();

        // The lines of the sub-items are styled apart and shown here, while their tab is open; the list
        // does not recycle its lines, see its panel
        TabList.ContainerPrepared += (_, e) =>
        {
            var line = TabList.Items[e.Index] as SettingNavItemVM;
            e.Container.Classes.Set("sub", line is { IsSubItem: true });
            e.Container.IsVisible = line?.IsShown ?? true;
        };

        this.WhenActivated(disposables =>
        {
            foreach (var line in ViewModel!.NavigationItems.Where(line => line.HasSubItems))
            {
                line.PropertyChanged += OnTabLineChanged;
                Disposable.Create(() => line.PropertyChanged -= OnTabLineChanged).DisposeWith(disposables);
            }
        });
    }

    // The last folding of each line of a sub-item, cancelled when the next folding starts.
    private readonly Dictionary<Control, CancellationTokenSource> _folds = new();

    // The sub-items of a tab unfold under it as it is opened and fold back as it is closed, and the line
    // down them grows and shrinks with them: the lines are cut at their edges while their height changes
    private void OnTabLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingNavItemVM.IsExpanded) || sender is not SettingNavItemVM tab)
            return;

        foreach (var subItem in ViewModel!.NavigationItems.Where(line => line.Parent == tab))
        {
            if (TabList.ContainerFromItem(subItem) is { } container)
                Fold(container, !tab.IsExpanded);
        }
    }

    private async void Fold(Control container, bool isFolded)
    {
        // Where a folding that is cut short has got to, before cancelling it gives the line its own values back
        var height = container.IsVisible ? container.Bounds.Height : 0;
        var opacity = container.IsVisible ? container.Opacity : 0;

        if (_folds.Remove(container, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        // Forward fill writes the collapsed values back to the control. Cancelling a completed
        // animation does not remove them: restore the row before measuring or showing it again.
        container.ClearValue(Layoutable.MaxHeightProperty);
        container.ClearValue(Visual.OpacityProperty);

        if (!Motion.IsEnabled)
        {
            container.IsVisible = !isFolded;
            return;
        }

        container.IsVisible = true;
        container.ClipToBounds = true;
        container.Measure(Size.Infinity);
        var fullHeight = container.DesiredSize.Height - container.Margin.Top - container.Margin.Bottom;

        var run = new CancellationTokenSource();
        _folds[container] = run;

        // An unfolded line ends with its own height, not one it has been animated to
        await new Animation
        {
            Duration = Motion.Get(MotionDuration.Medium),
            Easing = Motion.Ease(MotionEase.Standard),
            FillMode = isFolded ? FillMode.Forward : FillMode.None,
            Children =
            {
                Frame(0, height, opacity),
                Frame(1, isFolded ? 0 : fullHeight, isFolded ? 0 : 1)
            }
        }.RunAsync(container, run.Token);

        if (run.IsCancellationRequested)
            return;

        container.IsVisible = !isFolded;
        if (!isFolded && _folds.Remove(container))
            run.Dispose();
    }

    private static KeyFrame Frame(double cue, double height, double opacity) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(Layoutable.MaxHeightProperty, height), new Setter(Visual.OpacityProperty, opacity) }
    };

    // A sub-item that is picked already leads to its section again, as the page may have been scrolled since
    private void TabList_OnTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } vm && (e.Source as Control)?.DataContext is SettingNavItemVM { IsSubItem: true } item &&
            ReferenceEquals(item, vm.SelectedNavigationItem))
            vm.ShowSection(item);
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
