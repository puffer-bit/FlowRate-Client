using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Client.Avalonia.Localization;

namespace Client.Services;

/// <summary>
/// Shows a text of the string table in place of a missing value: the converter parameter is its key.
/// </summary>
public class TextOrDefaultConverter : IValueConverter
{
    public static TextOrDefaultConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string { Length: > 0 } ? value : Strings.Get(parameter as string ?? string.Empty);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
