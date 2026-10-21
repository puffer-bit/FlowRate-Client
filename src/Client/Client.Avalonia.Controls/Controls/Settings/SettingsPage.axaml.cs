using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.Settings;

/// <summary>
/// The body of one settings tab: a scrollable column of <see cref="SettingsHeader"/>s,
/// <see cref="SettingsCard"/>s and whatever else the tab shows, with the same spacing on every tab.
/// Controls marked with <see cref="SearchKeyProperty"/> can be scrolled to by <see cref="ScrollTo"/>.
/// A section, which is everything from a header to the next one, is folded away while its header
/// is collapsed; <see cref="CollapsedSectionsProperty"/> keeps which ones are.
/// </summary>
public class SettingsPage : ItemsControl
{
    /// <summary>
    /// Marks a control of the page as the place of a setting, so that the settings search can show it.
    /// </summary>
    public static readonly AttachedProperty<string?> SearchKeyProperty =
        AvaloniaProperty.RegisterAttached<SettingsPage, Control, string?>("SearchKey");

    /// <summary>
    /// The <see cref="SettingsHeader.SectionKey"/>s of the collapsed sections. Set on whatever hosts
    /// the pages and inherited by them; a page adds and removes keys as its headers are clicked.
    /// </summary>
    public static readonly AttachedProperty<ICollection<string>?> CollapsedSectionsProperty =
        AvaloniaProperty.RegisterAttached<SettingsPage, Control, ICollection<string>?>("CollapsedSections", inherits: true);

    static SettingsPage()
    {
        CollapsedSectionsProperty.Changed.AddClassHandler<SettingsPage>((page, _) => page.LoadCollapsedSections());
        SettingsHeader.IsCollapsedProperty.Changed.AddClassHandler<SettingsHeader>((header, _) =>
            (header.Parent as SettingsPage)?.OnHeaderToggled(header));
    }

    // What keeps each control of a collapsed section hidden. The control is hidden above its own
    // value and bindings, which are back once the entry is disposed of
    private readonly Dictionary<Control, FoldedItem> _folded = new();

    // Set while the page changes its headers itself
    private bool _isLoadingSections;

    // Cleared to fold and unfold at once, when the result has to be there straight away
    private bool _isAnimated = true;

    public static string? GetSearchKey(Control control) => control.GetValue(SearchKeyProperty);

    public static void SetSearchKey(Control control, string? value) => control.SetValue(SearchKeyProperty, value);

    public static ICollection<string>? GetCollapsedSections(Control control) => control.GetValue(CollapsedSectionsProperty);

    public static void SetCollapsedSections(Control control, ICollection<string>? value) =>
        control.SetValue(CollapsedSectionsProperty, value);

    /// <summary>
    /// Scrolls to the control marked with the key and, when it is a <see cref="SettingsRow"/>, flashes it.
    /// A collapsed section the control is in is shown first.
    /// </summary>
    /// <returns>False when the page has no such control, e.g. it is not laid out yet.</returns>
    public bool ScrollTo(string key)
    {
        foreach (var visual in this.GetVisualDescendants())
        {
            if (visual is not Control control || GetSearchKey(control) != key)
                continue;

            if (FindHeader(control) is { IsCollapsed: true } header)
            {
                // The control gets its place in this layout pass, there is no time to unfold it
                _isAnimated = false;
                header.SetCurrentValue(SettingsHeader.IsCollapsedProperty, false);
                _isAnimated = true;
                UpdateLayout();
            }

            control.BringIntoView();
            (control as SettingsRow)?.Highlight();
            return true;
        }

        return false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LoadCollapsedSections();
    }

    private void LoadCollapsedSections()
    {
        if (GetCollapsedSections(this) is not { } collapsed)
            return;

        _isLoadingSections = true;
        foreach (var item in Items)
        {
            if (item is SettingsHeader { SectionKey: { } key } header)
                header.SetCurrentValue(SettingsHeader.IsCollapsedProperty, collapsed.Contains(key));
        }
        _isLoadingSections = false;

        // The page opens with its sections as they were left, nothing is seen folding
        FoldSections(false);
    }

    private void OnHeaderToggled(SettingsHeader header)
    {
        if (_isLoadingSections)
            return;

        if (header.SectionKey is { } key && GetCollapsedSections(this) is { } collapsed)
        {
            if (!header.IsCollapsed)
                collapsed.Remove(key);
            else if (!collapsed.Contains(key))
                collapsed.Add(key);
        }

        FoldSections(_isAnimated && IsLoaded && Motion.IsEnabled);
    }

    private void FoldSections(bool isAnimated)
    {
        SettingsHeader? header = null;
        foreach (var item in Items)
        {
            if (item is SettingsHeader next)
                header = next;
            else if (item is Control control)
                SetFolded(control, header is { IsCollapsed: true }, isAnimated);
        }
    }

    private void SetFolded(Control control, bool isFolded, bool isAnimated)
    {
        if (isFolded == _folded.ContainsKey(control))
            return;

        if (!isFolded)
        {
            _folded.Remove(control, out var folded);
            folded!.Dispose();
            Presence.SetIsLeaving(control, false);

            if (isAnimated && control.IsVisible)
                Presence.Play(control, AppearEffect.Unfold);
            return;
        }

        var item = new FoldedItem();
        _folded.Add(control, item);

        if (!isAnimated || !control.IsVisible)
        {
            item.Hide(control);
            return;
        }

        // Hidden only after the fold, and then freed of what the fold has left on it
        Presence.SetLeave(control, LeaveEffect.Fold);
        Presence.SetIsLeaving(control, true);
        item.Timer = DispatcherTimer.RunOnce(() =>
        {
            item.Hide(control);
            Presence.SetIsLeaving(control, false);
        }, Presence.LeaveTime);
    }

    // The header of the section the control is in: the control itself when it is a header
    private SettingsHeader? FindHeader(Control control)
    {
        Visual? item = control;
        while (item != null && !ReferenceEquals((item as Control)?.Parent, this))
            item = item.GetVisualParent();

        SettingsHeader? header = null;
        foreach (var candidate in Items)
        {
            if (candidate is SettingsHeader next)
                header = next;
            if (ReferenceEquals(candidate, item))
                return header;
        }

        return null;
    }

    private sealed class FoldedItem : IDisposable
    {
        public IDisposable? Timer { get; set; }

        private IDisposable? _hidden;

        public void Hide(Control control) =>
            _hidden = control.SetValue(IsVisibleProperty, false, BindingPriority.Animation);

        public void Dispose()
        {
            Timer?.Dispose();
            _hidden?.Dispose();
        }
    }
}
