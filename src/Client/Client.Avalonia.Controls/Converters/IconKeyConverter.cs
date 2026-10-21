using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Client.Avalonia.Controls.Icons;

namespace Client.Avalonia.Controls.Converters;

/// <summary>
/// Turns the key of a DrawingImage resource from Client.Avalonia.Assets/IconImages.axaml into the
/// image, so that a view model names an icon without holding one.
/// </summary>
public class IconKeyConverter : IValueConverter
{
    public static readonly IconKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return IconResources.Get(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
