using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Metadata;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls;

/// <summary>
/// A card with a header that unfolds what it holds. The content grows out of the header and folds
/// back into it: the height of a clipping panel is animated from where it is to the height the
/// content wants, and is left to the layout again once it is there.
/// </summary>
[TemplatePart("PART_Header", typeof(Border))]
[TemplatePart("PART_Clip", typeof(Panel))]
public class ExpandableSection : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ExpandableSection, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ExpandableSection, string?>(nameof(Description));

    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<ExpandableSection, IImage?>(nameof(Icon));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<ExpandableSection, bool>(nameof(IsExpanded),
            defaultBindingMode: BindingMode.TwoWay);

    private Border? _header;
    private Panel? _clip;
    // Counts the folds, so that the end of one that was overtaken by the next does nothing
    private int _foldVersion;

    static ExpandableSection()
    {
        IsExpandedProperty.Changed.AddClassHandler<ExpandableSection>((section, change) =>
            section.OnIsExpandedChanged(change.GetNewValue<bool>()));
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

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_header != null)
        {
            _header.PointerReleased -= OnHeaderPointerReleased;
            _header.KeyDown -= OnHeaderKeyDown;
        }

        _header = e.NameScope.Find<Border>("PART_Header");
        if (_header != null)
        {
            _header.PointerReleased += OnHeaderPointerReleased;
            _header.KeyDown += OnHeaderKeyDown;
        }

        _clip = e.NameScope.Find<Panel>("PART_Clip");
        PseudoClasses.Set(":expanded", IsExpanded);

        if (_clip == null)
            return;

        // Shown as it is, without unfolding
        _clip.Transitions = null;
        _clip.Height = IsExpanded ? double.NaN : 0;
        _clip.IsVisible = IsExpanded;
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Only a click that ends over the header, the way a button takes it
        if (e.InitialPressMouseButton != MouseButton.Left || _header == null ||
            !new Rect(_header.Bounds.Size).Contains(e.GetPosition(_header)))
            return;

        SetCurrentValue(IsExpandedProperty, !IsExpanded);
        e.Handled = true;
    }

    private void OnHeaderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
            return;

        SetCurrentValue(IsExpandedProperty, !IsExpanded);
        e.Handled = true;
    }

    private void OnIsExpandedChanged(bool isExpanded)
    {
        PseudoClasses.Set(":expanded", isExpanded);

        if (_clip == null)
            return;

        var version = ++_foldVersion;
        var duration = Motion.Get(MotionDuration.Medium);

        // Where the panel is now: a fold under way is taken over from the height it has reached
        var from = _clip.IsVisible ? _clip.Bounds.Height : 0;
        var to = 0d;

        if (isExpanded)
        {
            _clip.IsVisible = true;

            var content = _clip.Children.FirstOrDefault();
            if (content != null)
            {
                content.Measure(new Size(_clip.Bounds.Width > 0 ? _clip.Bounds.Width : Bounds.Width, double.PositiveInfinity));
                to = content.DesiredSize.Height;
            }
        }

        if (duration == TimeSpan.Zero)
        {
            Settle(isExpanded);
            return;
        }

        // The jump to the start is not animated
        _clip.Transitions = null;
        _clip.Height = from;

        _clip.Transitions =
        [
            new DoubleTransition
            {
                Property = HeightProperty,
                Duration = duration,
                Easing = Motion.Ease(MotionEase.Standard)
            }
        ];
        _clip.Height = to;

        DispatcherTimer.RunOnce(() =>
        {
            if (version == _foldVersion)
                Settle(isExpanded);
        }, duration);
    }

    // The panel follows its content again, or leaves it out of the layout and of the focus
    private void Settle(bool isExpanded)
    {
        if (_clip == null)
            return;

        _clip.Transitions = null;
        _clip.Height = isExpanded ? double.NaN : 0;
        _clip.IsVisible = isExpanded;
    }
}
