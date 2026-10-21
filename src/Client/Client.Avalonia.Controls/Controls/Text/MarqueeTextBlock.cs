using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace Client.Avalonia.Controls.Text;

public enum MarqueeTrigger
{
    /// <summary>Scrolls while the pointer is over the text or over the list item that contains it.</summary>
    PointerOver,
    /// <summary>Scrolls all the time while the text does not fit.</summary>
    Always,
}

/// <summary>
/// A single-line <see cref="TextBlock"/> that scrolls its text like a ticker when it does not fit.
/// At rest it looks like a regular trimmed TextBlock, and all TextBlock styles apply to it.
/// </summary>
public class MarqueeTextBlock : TextBlock
{
    public static readonly StyledProperty<MarqueeTrigger> TriggerProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, MarqueeTrigger>(nameof(Trigger));

    /// <summary>Scroll speed in pixels per second.</summary>
    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(Speed), 40);

    /// <summary>Space between the end of the text and its next copy.</summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(Gap), 40);

    /// <summary>How long the text stays at its start before every pass.</summary>
    public static readonly StyledProperty<TimeSpan> PauseProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, TimeSpan>(nameof(Pause), TimeSpan.FromSeconds(1.2));

    public MarqueeTrigger Trigger
    {
        get => GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    public double Speed
    {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public TimeSpan Pause
    {
        get => GetValue(PauseProperty);
        set => SetValue(PauseProperty, value);
    }

    // Styles written for TextBlock (classes, trimming, foreground of selected items) keep working
    protected override Type StyleKeyOverride => typeof(TextBlock);

    private TextLayout? _fullLayout;
    private Control? _hoverSource;
    private bool _isRunning;
    private TimeSpan? _startTime;
    private double _offset;

    static MarqueeTextBlock()
    {
        TextTrimmingProperty.OverrideDefaultValue<MarqueeTextBlock>(TextTrimming.CharacterEllipsis);
        TextWrappingProperty.OverrideDefaultValue<MarqueeTextBlock>(TextWrapping.NoWrap);
        MaxLinesProperty.OverrideDefaultValue<MarqueeTextBlock>(1);
        ClipToBoundsProperty.OverrideDefaultValue<MarqueeTextBlock>(true);
    }

    private double AvailableTextWidth => Bounds.Width - Padding.Left - Padding.Right;

    private bool IsOverflowing => FullLayout.WidthIncludingTrailingWhitespace > AvailableTextWidth + 0.5;

    // The untrimmed text, measured and drawn while scrolling
    private TextLayout FullLayout => _fullLayout ??= new TextLayout(
        Text,
        new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
        FontSize,
        Foreground,
        textWrapping: TextWrapping.NoWrap,
        textTrimming: TextTrimming.None,
        textDecorations: TextDecorations,
        letterSpacing: LetterSpacing,
        fontFeatures: FontFeatures);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Users hover rows, not the text itself, so the containing list item also starts the scroll
        _hoverSource = this.FindAncestorOfType<ListBoxItem>() ?? (Control)this;
        _hoverSource.PropertyChanged += OnHoverSourcePropertyChanged;
        UpdateRunning();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_hoverSource != null)
        {
            _hoverSource.PropertyChanged -= OnHoverSourcePropertyChanged;
            _hoverSource = null;
        }
        Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontStyleProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStretchProperty ||
            change.Property == ForegroundProperty ||
            change.Property == TextDecorationsProperty ||
            change.Property == LetterSpacingProperty ||
            change.Property == FontFeaturesProperty)
        {
            _fullLayout?.Dispose();
            _fullLayout = null;
            UpdateRunning();
        }
        else if (change.Property == BoundsProperty || change.Property == TriggerProperty)
        {
            UpdateRunning();
        }
    }

    private void OnHoverSourcePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsPointerOverProperty)
            UpdateRunning();
    }

    private void UpdateRunning()
    {
        bool shouldRun = _hoverSource != null
                         && !string.IsNullOrEmpty(Text)
                         && (Trigger == MarqueeTrigger.Always || _hoverSource.IsPointerOver)
                         && IsOverflowing;

        if (shouldRun)
            Start();
        else
            Stop();
    }

    private void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _startTime = null;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnAnimationFrame);
    }

    private void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _offset = 0;
        InvalidateVisual();
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        if (!_isRunning)
            return;

        _startTime ??= time;

        // One cycle: a pause at the start, then the text travels by its width plus the gap
        double pause = Pause.TotalSeconds;
        double distance = FullLayout.WidthIncludingTrailingWhitespace + Gap;
        double travel = distance / Math.Max(Speed, 1);
        double position = (time - _startTime.Value).TotalSeconds % (pause + travel);

        _offset = position < pause ? 0 : (position - pause) * Speed;
        InvalidateVisual();

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnAnimationFrame);
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        // At rest (and during the pause) it is the regular trimmed TextBlock
        if (!_isRunning || _offset <= 0)
        {
            base.RenderTextLayout(context, origin);
            return;
        }

        var layout = FullLayout;
        var textArea = new Rect(Bounds.Size).Deflate(Padding);

        using (context.PushClip(textArea))
        {
            double x = origin.X - _offset;
            layout.Draw(context, new Point(x, origin.Y));
            // The next copy slides in behind the gap, so the loop has no visible jump
            layout.Draw(context, new Point(x + layout.WidthIncludingTrailingWhitespace + Gap, origin.Y));
        }
    }
}
