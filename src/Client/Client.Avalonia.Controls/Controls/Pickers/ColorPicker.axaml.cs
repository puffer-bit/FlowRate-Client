using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Client.Avalonia.Controls.Pickers;

/// <summary>
/// A field that shows an opaque color and its "#RRGGBB" code and opens a panel to pick another:
/// a square of saturation and brightness, a strip of hues, the code to type and ready colors.
/// The color follows every step of a drag.
/// </summary>
[TemplatePart(PopupPart, typeof(Popup))]
[TemplatePart(SquarePart, typeof(ColorSquare))]
[TemplatePart(HexPart, typeof(TextBox))]
[TemplatePart(PalettePart, typeof(ItemsControl))]
[PseudoClasses(":dropdownopen", ":pressed")]
public class ColorPicker : TemplatedControl
{
    private const string PopupPart = "PART_Popup";
    private const string SquarePart = "PART_Square";
    private const string HexPart = "PART_Hex";
    private const string PalettePart = "PART_Palette";

    // A press on the field that closed the panel a moment ago must not open it again
    private const long DismissWindowMilliseconds = 150;

    // Two rows of six: the spectrum, then pink, a gray, black and white. Declared before the
    // property that takes it as its default: static fields are set in the order they are written
    private static readonly IReadOnlyList<Color> DefaultPalette =
    [
        Color.Parse("#E5484D"), Color.Parse("#F76B15"), Color.Parse("#FFC53D"),
        Color.Parse("#46A758"), Color.Parse("#12A594"), Color.Parse("#00A2C7"),
        Color.Parse("#3E63DD"), Color.Parse("#6E56CF"), Color.Parse("#D6409F"),
        Color.Parse("#8B8D98"), Color.Parse("#1C2024"), Color.Parse("#FFFFFF")
    ];

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorPicker, Color>(nameof(Color), Colors.White,
            defaultBindingMode: BindingMode.TwoWay, coerce: (_, color) => Color.FromRgb(color.R, color.G, color.B));

    // The color as the panel edits it. Hue and saturation are kept apart from the color: gray and
    // black have none, and the thumbs would jump to the corner whenever a drag passes through them
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorPicker, double>(nameof(Hue));

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorPicker, double>(nameof(Saturation));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ColorPicker, double>(nameof(Value), 1);

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<ColorPicker, bool>(nameof(IsDropDownOpen));

    /// <summary>
    /// Ready colors offered under the code.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Color>> PaletteProperty =
        AvaloniaProperty.Register<ColorPicker, IReadOnlyList<Color>>(nameof(Palette), DefaultPalette);

    public static readonly DirectProperty<ColorPicker, string> HexTextProperty =
        AvaloniaProperty.RegisterDirect<ColorPicker, string>(nameof(HexText), picker => picker.HexText);

    public static readonly DirectProperty<ColorPicker, IBrush> ColorBrushProperty =
        AvaloniaProperty.RegisterDirect<ColorPicker, IBrush>(nameof(ColorBrush), picker => picker.ColorBrush);

    private string _hexText = "#FFFFFF";
    private IBrush _colorBrush = Brushes.White;

    private Popup? _popup;
    private ColorSquare? _square;
    private TextBox? _hexBox;
    private ItemsControl? _palette;

    // Set while one side of the color is brought in line with the other
    private bool _isSyncing;
    // Set while the code is being typed, so the box is not rewritten under the caret
    private bool _isTypingHex;

    private long? _closedAt;
    private bool _isPressDismissing;

    static ColorPicker()
    {
        FocusableProperty.OverrideDefaultValue<ColorPicker>(true);
    }

    public ColorPicker()
    {
        ShowColor(Color);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public IReadOnlyList<Color> Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>
    /// The color as "#RRGGBB".
    /// </summary>
    public string HexText
    {
        get => _hexText;
        private set => SetAndRaise(HexTextProperty, ref _hexText, value);
    }

    public IBrush ColorBrush
    {
        get => _colorBrush;
        private set => SetAndRaise(ColorBrushProperty, ref _colorBrush, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_popup != null)
        {
            _popup.Opened -= OnPopupOpened;
            _popup.Closed -= OnPopupClosed;
        }

        if (_hexBox != null)
        {
            _hexBox.TextChanged -= OnHexTextChanged;
            _hexBox.LostFocus -= OnHexLostFocus;
            _hexBox.KeyDown -= OnHexKeyDown;
        }

        _palette?.RemoveHandler(Button.ClickEvent, OnPaletteClick);

        _popup = e.NameScope.Find<Popup>(PopupPart);
        _square = e.NameScope.Find<ColorSquare>(SquarePart);
        _hexBox = e.NameScope.Find<TextBox>(HexPart);
        _palette = e.NameScope.Find<ItemsControl>(PalettePart);

        if (_popup != null)
        {
            _popup.Opened += OnPopupOpened;
            _popup.Closed += OnPopupClosed;
        }

        if (_hexBox != null)
        {
            _hexBox.Text = HexText;
            _hexBox.TextChanged += OnHexTextChanged;
            _hexBox.LostFocus += OnHexLostFocus;
            _hexBox.KeyDown += OnHexKeyDown;
        }

        _palette?.AddHandler(Button.ClickEvent, OnPaletteClick);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColorProperty)
        {
            if (!_isSyncing)
                ShowColor(change.GetNewValue<Color>());
        }
        else if (change.Property == HueProperty || change.Property == SaturationProperty ||
                 change.Property == ValueProperty)
        {
            if (!_isSyncing)
                TakeColorFromThumbs();
        }
        else if (change.Property == IsDropDownOpenProperty)
        {
            PseudoClasses.Set(":dropdownopen", change.GetNewValue<bool>());
        }
    }

    // The color was set from outside, typed or picked from the ready ones: the thumbs follow it
    private void ShowColor(Color color)
    {
        var hsv = color.ToHsv();

        _isSyncing = true;
        // A gray keeps the hue it was reached from, and black the saturation as well
        if (hsv.V > 0)
        {
            if (hsv.S > 0)
                SetCurrentValue(HueProperty, hsv.H);

            SetCurrentValue(SaturationProperty, hsv.S);
        }

        SetCurrentValue(ValueProperty, hsv.V);
        _isSyncing = false;

        ShowCode(color);
    }

    private void TakeColorFromThumbs()
    {
        var color = new HsvColor(1, Hue, Saturation, Value).ToRgb();

        _isSyncing = true;
        SetCurrentValue(ColorProperty, color);
        _isSyncing = false;

        ShowCode(Color);
    }

    private void ShowCode(Color color)
    {
        HexText = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        ColorBrush = new ImmutableSolidColorBrush(color);

        if (_hexBox != null && !_isTypingHex)
            _hexBox.Text = HexText;
    }

    private void OnHexTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Applied as soon as it is a whole code; the short "#RGB" form waits for Enter, since
        // it is also the first half of a long one
        if (_hexBox?.IsFocused != true || !TryParseHex(_hexBox.Text, allowShort: false, out var color))
            return;

        _isTypingHex = true;
        SetCurrentValue(ColorProperty, color);
        _isTypingHex = false;
    }

    private void OnHexLostFocus(object? sender, RoutedEventArgs e) => CommitHex();

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitHex();
        _hexBox?.SelectAll();
        e.Handled = true;
    }

    // Whatever cannot be read as a color is thrown away and the box shows the color again
    private void CommitHex()
    {
        if (_hexBox == null)
            return;

        if (TryParseHex(_hexBox.Text, allowShort: true, out var color))
            SetCurrentValue(ColorProperty, color);

        _hexBox.Text = HexText;
    }

    private static bool TryParseHex(string? text, bool allowShort, out Color color)
    {
        color = default;

        var digits = (text ?? string.Empty).Trim().TrimStart('#');
        if (digits.Length == 3 && allowShort)
            digits = string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]);

        if (digits.Length != 6 ||
            !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            return false;

        color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    private void OnPaletteClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control { DataContext: Color color })
            SetCurrentValue(ColorProperty, color);
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        // The arrow keys work at once, and Escape has a focused element to come from
        _square?.Focus();

        if (_popup?.Child is { } content)
        {
            content.KeyDown -= OnPopupKeyDown;
            content.KeyDown += OnPopupKeyDown;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _closedAt = Environment.TickCount64;
        SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void OnPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        SetCurrentValue(IsDropDownOpenProperty, false);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // The same press has just closed the panel by landing outside of it
        _isPressDismissing = _closedAt is { } closedAt &&
                             Environment.TickCount64 - closedAt < DismissWindowMilliseconds;
        PseudoClasses.Set(":pressed", true);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!PseudoClasses.Contains(":pressed"))
            return;

        PseudoClasses.Set(":pressed", false);

        if (!_isPressDismissing && new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            SetCurrentValue(IsDropDownOpenProperty, true);

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        PseudoClasses.Set(":pressed", false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || e.Key is not (Key.Enter or Key.Space or Key.Down))
            return;

        SetCurrentValue(IsDropDownOpenProperty, true);
        e.Handled = true;
    }
}
