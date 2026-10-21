using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Client.Avalonia.Controls.Converters;

public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isEntered = value is bool b && b;
        return isEntered ? 0.5 : 1.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
