using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Client.Avalonia.Controls.Avatars;

/// <summary>
/// Draws a short text centered by the outline of its glyphs rather than by its line box. A line box
/// keeps room for descenders, so a capital centered by it sits visibly above the middle of a circle.
/// </summary>
public class AvatarInitial : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<AvatarInitial, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<AvatarInitial>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<AvatarInitial>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<AvatarInitial>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner<AvatarInitial>();

    private Geometry? _geometry;

    static AvatarInitial()
    {
        AffectsRender<AvatarInitial>(ForegroundProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty || change.Property == FontFamilyProperty
            || change.Property == FontSizeProperty || change.Property == FontWeightProperty)
        {
            _geometry = null;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Text) || Foreground == null)
            return;

        _geometry ??= new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle.Normal, FontWeight), FontSize, Foreground)
            .BuildGeometry(new Point());

        if (_geometry == null)
            return;

        var glyphs = _geometry.Bounds;
        var offset = new Point(
            (Bounds.Width - glyphs.Width) / 2 - glyphs.X,
            (Bounds.Height - glyphs.Height) / 2 - glyphs.Y);

        using (context.PushTransform(Matrix.CreateTranslation(offset)))
            context.DrawGeometry(Foreground, null, _geometry);
    }
}
