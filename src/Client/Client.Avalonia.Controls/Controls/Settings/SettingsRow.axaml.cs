using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.Settings;

/// <summary>Where a <see cref="SettingsRow"/> puts its content</summary>
public enum SettingsRowContentPlacement
{
    /// <summary>At the right edge, on the line of the title: a check box, a combo box, a button</summary>
    Right,

    /// <summary>Under the title, across the whole row: wide content such as a meter or a list</summary>
    Below
}

/// <summary>
/// One setting inside a <see cref="SettingsCard"/>: the title and an optional description on the left,
/// the control that changes the setting (the content) on the right or below.
/// Rows after the first one draw a separator above themselves.
/// <see cref="IndentLevel"/> nests a row under the one above it, and <see cref="IsExpanded"/>
/// folds a nested row away while the setting above it is off.
/// </summary>
public class SettingsRow : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Title));

    /// <summary>A small picture before the title, for the rows that name a thing rather than a choice.</summary>
    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<SettingsRow, IImage?>(nameof(Icon));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Description));

    public static readonly StyledProperty<SettingsRowContentPlacement> ContentPlacementProperty =
        AvaloniaProperty.Register<SettingsRow, SettingsRowContentPlacement>(nameof(ContentPlacement));

    public static readonly StyledProperty<int> IndentLevelProperty =
        AvaloniaProperty.Register<SettingsRow, int>(nameof(IndentLevel));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SettingsRow, bool>(nameof(IsExpanded), true);

    static SettingsRow()
    {
        IsExpandedProperty.Changed.AddClassHandler<SettingsRow>((row, change) =>
        {
            if (change.GetNewValue<bool>())
                row.Expand();
            else
                row.Collapse();
        });

        TappedEvent.AddClassHandler<SettingsRow>((row, e) => row.OnRowTapped(e));
    }

    private IDisposable? _collapseTimer;

    // Long enough to be noticed after the scroll, short enough not to look like a state
    private static readonly TimeSpan HighlightTime = TimeSpan.FromSeconds(1.5);
    private IDisposable? _highlightTimer;

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public SettingsRowContentPlacement ContentPlacement
    {
        get => GetValue(ContentPlacementProperty);
        set => SetValue(ContentPlacementProperty, value);
    }

    /// <summary>
    /// How deep the setting is nested under the rows above it: 0 for an independent setting,
    /// 1 or 2 for one that refines the setting above. Nested rows are shifted to the right.
    /// </summary>
    public int IndentLevel
    {
        get => GetValue(IndentLevelProperty);
        set => SetValue(IndentLevelProperty, value);
    }

    /// <summary>
    /// Whether the row is shown. A nested row is bound to the setting above it: it unfolds when
    /// that setting is switched on and folds away when it is switched off.
    /// </summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Flashes the row to show where a setting is: sets the :highlighted pseudo-class for a moment.
    /// </summary>
    public void Highlight()
    {
        _highlightTimer?.Dispose();
        PseudoClasses.Set(":highlighted", true);
        _highlightTimer = DispatcherTimer.RunOnce(() => PseudoClasses.Set(":highlighted", false), HighlightTime);
    }

    // The title, the description and the empty space work as the label of a check box or a switch:
    // a tap anywhere on the row switches it. A tap on the control itself is left to the control.
    private void OnRowTapped(TappedEventArgs e)
    {
        if (e.Handled || Content is not ToggleButton toggle || !toggle.IsEffectivelyEnabled)
            return;
        if (e.Source is Visual source && (source == toggle || toggle.IsVisualAncestorOf(source)))
            return;

        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, toggle.IsChecked != true);
        e.Handled = true;
    }

    private void Expand()
    {
        _collapseTimer?.Dispose();
        _collapseTimer = null;
        Presence.SetIsLeaving(this, false);

        SetCurrentValue(IsVisibleProperty, true);
        Presence.Play(this, AppearEffect.Unfold);
    }

    private void Collapse()
    {
        _collapseTimer?.Dispose();
        _collapseTimer = null;

        // A row that is not on the screen yet has nothing to fold: it starts hidden
        if (!IsLoaded)
        {
            SetCurrentValue(IsVisibleProperty, false);
            return;
        }

        // Hidden only after the fold, and then freed of what the fold has left on it
        Presence.SetLeave(this, LeaveEffect.Fold);
        Presence.SetIsLeaving(this, true);
        _collapseTimer = DispatcherTimer.RunOnce(() =>
        {
            SetCurrentValue(IsVisibleProperty, false);
            Presence.SetIsLeaving(this, false);
        }, Presence.LeaveTime);
    }
}
