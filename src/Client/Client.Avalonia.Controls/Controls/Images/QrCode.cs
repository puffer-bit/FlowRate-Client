using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QRCoder;

namespace Client.Avalonia.Controls.Images;

/// <summary>
/// A QR code of <see cref="Text"/>, drawn square into the space it gets. Always dark on white with the quiet zone
/// around it, whatever the theme: scanners read inverted or borderless codes badly.
/// </summary>
public sealed class QrCode : Control
{
    // The modules of white the standard asks for around the code
    private const int QuietZone = 4;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<QrCode, string?>(nameof(Text));

    private bool[,]? _modules;

    static QrCode()
    {
        AffectsRender<QrCode>(TextProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
            _modules = Encode(Text);
    }

    public override void Render(DrawingContext context)
    {
        if (_modules is not { } modules)
            return;

        var count = modules.GetLength(0);
        var total = count + QuietZone * 2;
        var side = Math.Min(Bounds.Width, Bounds.Height);

        // Whole pixels per module keep the edges sharp, which scanners need more than the full size
        var module = Math.Max(1, Math.Floor(side / total));
        var size = module * total;
        var origin = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);

        context.FillRectangle(Brushes.White, new Rect(origin, new Size(size, size)));

        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            for (var row = 0; row < count; row++)
            for (var column = 0; column < count; column++)
            {
                if (!modules[row, column])
                    continue;

                var x = origin.X + (column + QuietZone) * module;
                var y = origin.Y + (row + QuietZone) * module;
                draw.BeginFigure(new Point(x, y), true);
                draw.LineTo(new Point(x + module, y));
                draw.LineTo(new Point(x + module, y + module));
                draw.LineTo(new Point(x, y + module));
                draw.EndFigure(true);
            }
        }

        context.DrawGeometry(Brushes.Black, null, geometry);
    }

    private static bool[,]? Encode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        // QRCoder adds the quiet zone itself; it is drawn here instead
        var rows = data.ModuleMatrix;
        var count = rows.Count - QuietZone * 2;
        var modules = new bool[count, count];
        for (var row = 0; row < count; row++)
        for (var column = 0; column < count; column++)
            modules[row, column] = rows[row + QuietZone][column + QuietZone];

        return modules;
    }
}
