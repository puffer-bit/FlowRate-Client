using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Client.Avalonia.Controls.Converters
{
    public class FitSquarelyWithinAspectRatioConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is Rect bounds ? Math.Min(bounds.Width, bounds.Height) : AvaloniaProperty.UnsetValue;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
