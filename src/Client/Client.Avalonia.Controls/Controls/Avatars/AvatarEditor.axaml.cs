using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Client.Avalonia.Controls.Avatars;

/// <summary>Shared desktop and touch photo editor; image decoding stays with the caller.</summary>
public partial class AvatarEditor : UserControl
{
    public event Action? Accepted;
    public event Action? Cancelled;
    public event Action? ResetRequested;
    public event Action<bool>? TransformRequested;
    public Rect Crop => CropCanvas.Crop;

    public AvatarEditor()
    {
        InitializeComponent();
        ZoomSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                CropCanvas.Zoom = ZoomSlider.Value;
        };
        RotateButton.Click += (_, _) => TransformRequested?.Invoke(false);
        MirrorButton.Click += (_, _) => TransformRequested?.Invoke(true);
        ResetButton.Click += (_, _) => ResetRequested?.Invoke();
        SaveButton.Click += (_, _) =>
        {
            IsEnabled = false;
            Accepted?.Invoke();
        };
        CancelButton.Click += (_, _) => Cancelled?.Invoke();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Cancelled?.Invoke();
                e.Handled = true;
            }
        };
    }

    public void SetPicture(IImage picture)
    {
        CropCanvas.SetPicture(picture);
        ResetCrop();
    }

    private void ResetCrop()
    {
        ZoomSlider.Value = 1;
        CropCanvas.Reset();
    }
}

public sealed class AvatarCropCanvas : Control
{
    private IImage? _picture;
    private double _zoom = 1;
    private double _centerX;
    private double _centerY;
    private Point? _lastPointer;

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = value;
            ClampCenter();
            InvalidateVisual();
        }
    }

    private double CropSide => _picture == null ? 1 : Math.Min(_picture.Size.Width, _picture.Size.Height) / _zoom;

    public Rect Crop => _picture == null ? default : new Rect(
        (_centerX - CropSide / 2) / _picture.Size.Width,
        (_centerY - CropSide / 2) / _picture.Size.Height,
        CropSide / _picture.Size.Width, CropSide / _picture.Size.Height);

    public void SetPicture(IImage picture)
    {
        _picture = picture;
        Reset();
    }

    public void Reset()
    {
        _zoom = 1;
        _centerX = (_picture?.Size.Width ?? 1) / 2;
        _centerY = (_picture?.Size.Height ?? 1) / 2;
        InvalidateVisual();
    }

    private void ClampCenter()
    {
        if (_picture == null)
            return;
        var half = CropSide / 2;
        _centerX = Math.Clamp(_centerX, half, _picture.Size.Width - half);
        _centerY = Math.Clamp(_centerY, half, _picture.Size.Height - half);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);
        if (_picture == null)
            return;
        var source = new Rect(_centerX - CropSide / 2, _centerY - CropSide / 2, CropSide, CropSide);
        context.DrawImage(_picture, source, bounds);
        var corners = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(bounds), new EllipseGeometry(bounds));
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, corners);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.5), bounds.Deflate(1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastPointer is not { } last || Bounds.Width <= 0)
            return;
        var current = e.GetPosition(this);
        _centerX -= (current.X - last.X) * CropSide / Bounds.Width;
        _centerY -= (current.Y - last.Y) * CropSide / Bounds.Height;
        _lastPointer = current;
        ClampCenter();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _lastPointer = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _lastPointer = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = CropSide / 30;
        switch (e.Key)
        {
            case Key.Left: _centerX -= step; break;
            case Key.Right: _centerX += step; break;
            case Key.Up: _centerY -= step; break;
            case Key.Down: _centerY += step; break;
            default: return;
        }
        ClampCenter();
        InvalidateVisual();
        e.Handled = true;
    }
}
