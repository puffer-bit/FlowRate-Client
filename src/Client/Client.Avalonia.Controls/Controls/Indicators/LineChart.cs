using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Client.Avalonia.Controls.Indicators;

/// <summary>
/// A small graph of the last readings of one or two figures, the newest on the right. A null
/// reading breaks the line. The scale starts at zero and grows with the highest reading shown.
/// </summary>
public class LineChart : Control
{
    // Room on the left for the labels of the scale
    private const double LabelWidth = 40;
    private const double LabelGap = 6;

    public static readonly StyledProperty<IReadOnlyList<double?>?> ValuesProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<double?>?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyList<double?>?> SecondaryValuesProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<double?>?>(nameof(SecondaryValues));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<LineChart, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> SecondaryStrokeProperty =
        AvaloniaProperty.Register<LineChart, IBrush?>(nameof(SecondaryStroke));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<LineChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<LineChart, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<LineChart, int>(nameof(Capacity), 60);

    public static readonly StyledProperty<double> MinimumMaximumProperty =
        AvaloniaProperty.Register<LineChart, double>(nameof(MinimumMaximum), 1);

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<LineChart, string>(nameof(Unit), "");

    /// <summary>
    /// The readings, oldest first.
    /// </summary>
    public IReadOnlyList<double?>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// A second line on the same scale, drawn under the first.
    /// </summary>
    public IReadOnlyList<double?>? SecondaryValues
    {
        get => GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? SecondaryStroke
    {
        get => GetValue(SecondaryStrokeProperty);
        set => SetValue(SecondaryStrokeProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>
    /// The brush of the labels of the scale.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// How many readings fit the width; fewer start from the right.
    /// </summary>
    public int Capacity
    {
        get => GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    /// <summary>
    /// The lowest top of the scale, so that small readings do not fill the graph.
    /// </summary>
    public double MinimumMaximum
    {
        get => GetValue(MinimumMaximumProperty);
        set => SetValue(MinimumMaximumProperty, value);
    }

    /// <summary>
    /// Written after the numbers of the scale.
    /// </summary>
    public string Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    static LineChart()
    {
        AffectsRender<LineChart>(ValuesProperty, SecondaryValuesProperty, StrokeProperty, SecondaryStrokeProperty,
            GridBrushProperty, ForegroundProperty, CapacityProperty, MinimumMaximumProperty, UnitProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(LabelWidth + LabelGap, 6, Math.Max(0, Bounds.Width - LabelWidth - LabelGap),
            Math.Max(0, Bounds.Height - 12));

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var maximum = NiceMaximum(Math.Max(MinimumMaximum, Highest(Values, SecondaryValues) * 1.15));

        DrawScale(context, bounds, maximum);

        if (SecondaryStroke != null)
            DrawLine(context, bounds, maximum, SecondaryValues, SecondaryStroke);

        if (Stroke != null)
            DrawLine(context, bounds, maximum, Values, Stroke);
    }

    private void DrawScale(DrawingContext context, Rect bounds, double maximum)
    {
        var pen = GridBrush == null ? null : new Pen(GridBrush, 1, new DashStyle([2, 3], 0));
        var typeface = new Typeface(FontFamily.Default);

        for (var step = 0; step <= 2; step++)
        {
            var value = maximum * step / 2;
            var y = Math.Round(bounds.Bottom - bounds.Height * step / 2) + 0.5;

            if (pen != null)
                context.DrawLine(pen, new Point(bounds.Left, y), new Point(bounds.Right, y));

            if (Foreground == null)
                continue;

            var text = new FormattedText($"{value:0.#} {Unit}".TrimEnd(), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, Foreground);

            context.DrawText(text, new Point(LabelWidth - text.Width, y - text.Height / 2));
        }
    }

    private void DrawLine(DrawingContext context, Rect bounds, double maximum, IReadOnlyList<double?>? values,
        IBrush stroke)
    {
        if (values == null || values.Count == 0)
            return;

        var capacity = Math.Max(2, Capacity);
        var spacing = bounds.Width / (capacity - 1);
        // The newest reading sits on the right edge
        var first = capacity - Math.Min(values.Count, capacity);
        var skipped = values.Count - (capacity - first);

        var line = new StreamGeometry();
        var area = new StreamGeometry();

        using (var lineContext = line.Open())
        using (var areaContext = area.Open())
        {
            var isDrawing = false;
            var segmentStart = 0.0;
            var last = new Point();

            for (var index = skipped; index < values.Count; index++)
            {
                var x = bounds.Left + (first + index - skipped) * spacing;

                if (values[index] is not { } value)
                {
                    if (isDrawing)
                    {
                        lineContext.EndFigure(false);
                        CloseArea(areaContext, last, segmentStart, bounds.Bottom);
                    }

                    isDrawing = false;
                    continue;
                }

                var point = new Point(x, bounds.Bottom - bounds.Height * Math.Clamp(value / maximum, 0, 1));

                if (!isDrawing)
                {
                    lineContext.BeginFigure(point, false);
                    areaContext.BeginFigure(new Point(x, bounds.Bottom), true);
                    areaContext.LineTo(point);
                    segmentStart = x;
                    isDrawing = true;
                }
                else
                {
                    lineContext.LineTo(point);
                    areaContext.LineTo(point);
                }

                last = point;
            }

            if (isDrawing)
            {
                lineContext.EndFigure(false);
                CloseArea(areaContext, last, segmentStart, bounds.Bottom);
            }
        }

        if (stroke is ISolidColorBrush solid)
            context.DrawGeometry(new SolidColorBrush(solid.Color, 0.15), null, area);

        context.DrawGeometry(null, new Pen(stroke, 1.5, lineJoin: PenLineJoin.Round), line);
    }

    /// <summary>
    /// Ends the filled area under a piece of the line at the bottom of the graph.
    /// </summary>
    private static void CloseArea(StreamGeometryContext context, Point last, double start, double bottom)
    {
        context.LineTo(new Point(last.X, bottom));
        context.LineTo(new Point(start, bottom));
        context.EndFigure(true);
    }

    private static double Highest(params IReadOnlyList<double?>?[] series) =>
        series.Where(values => values != null)
            .SelectMany(values => values!)
            .Select(value => value ?? 0)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// The value rounded up to 1, 2 or 5 times a power of ten, so the scale reads well.
    /// </summary>
    private static double NiceMaximum(double value)
    {
        if (value <= 0)
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var fraction = value / magnitude;

        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * magnitude;
    }
}
