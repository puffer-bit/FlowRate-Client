using Avalonia;
using Avalonia.Controls;

namespace Client.Avalonia.Controls.Text;

/// <summary>
/// Puts its second child right after the last character of its first child, a <see cref="TextBlock"/>
/// that may wrap: on the last line when there is room left, on a line of its own otherwise.
/// The text stays a plain TextBlock, so its selection and its Text are not touched.
/// </summary>
public class TrailingMarkPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TrailingMarkPanel, double>(nameof(Spacing), 4);

    static TrailingMarkPanel()
    {
        AffectsMeasure<TrailingMarkPanel>(SpacingProperty);
    }

    /// <summary>
    /// The gap between the end of the text and the mark.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
            return default;

        var text = Children[0];
        text.Measure(availableSize);
        var textSize = text.DesiredSize;

        if (Children.Count < 2 || !Children[1].IsVisible)
            return textSize;

        var mark = Children[1];
        mark.Measure(Size.Infinity);
        var markSize = mark.DesiredSize;

        double markEnd = GetLastLine(text).End + Spacing + markSize.Width;
        return markEnd <= availableSize.Width
            ? new Size(Math.Max(textSize.Width, markEnd), textSize.Height)
            : new Size(Math.Max(textSize.Width, markSize.Width), textSize.Height + markSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
            return finalSize;

        var text = Children[0];
        double textHeight = text.DesiredSize.Height;
        text.Arrange(new Rect(0, 0, finalSize.Width, textHeight));

        if (Children.Count < 2 || !Children[1].IsVisible)
            return finalSize;

        var mark = Children[1];
        var markSize = mark.DesiredSize;
        // Asked again: the text may have been laid out anew for the final width
        var (end, lineHeight) = GetLastLine(text);

        double x = end + Spacing;
        if (x + markSize.Width <= finalSize.Width)
            mark.Arrange(new Rect(new Point(x, textHeight - lineHeight + (lineHeight - markSize.Height) / 2), markSize));
        else
            mark.Arrange(new Rect(new Point(0, textHeight), markSize));

        return finalSize;
    }

    // Where the last line of the text ends, from the left edge of the text block, and how tall that line is
    private static (double End, double Height) GetLastLine(Control text)
    {
        if (text is not TextBlock textBlock || textBlock.TextLayout.TextLines.Count == 0)
            return (text.DesiredSize.Width, text.DesiredSize.Height);

        var line = textBlock.TextLayout.TextLines[^1];
        return (textBlock.Padding.Left + line.WidthIncludingTrailingWhitespace, line.Height);
    }
}
