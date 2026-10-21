using System;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Client.Avalonia.Controls.Converters;

public class BoolToGridLengthConverter : IValueConverter
{
    public GridLength TrueLength { get; set; } = new GridLength(50);
    public GridLength FalseLength { get; set; } = new GridLength(0);

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? TrueLength : FalseLength;
        }
        return FalseLength;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is GridLength gl)
        {
            return gl.Value > 0;
        }
        return false;
    }
}