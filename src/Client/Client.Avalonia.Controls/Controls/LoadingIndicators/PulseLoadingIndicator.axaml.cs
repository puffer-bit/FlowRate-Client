using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Client.Avalonia.Controls.LoadingIndicators;

public partial class PulseLoadingIndicator : UserControl
{
    public static readonly StyledProperty<IBrush?> ColorProperty =
        AvaloniaProperty.Register<PulseLoadingIndicator, IBrush?>(nameof(Color));

    public IBrush? Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public PulseLoadingIndicator()
    {
        InitializeComponent();
    }
}