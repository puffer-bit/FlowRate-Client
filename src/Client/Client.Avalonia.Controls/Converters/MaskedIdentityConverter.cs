using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Client.Avalonia.Controls.Converters;

/// <summary>
/// Shows only the edges of a user identity, enough to tell saved accounts apart without exposing it.
/// </summary>
public class MaskedIdentityConverter : IValueConverter
{
    private const int VisibleChars = 4;
    private const string Mask = "••••••••";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string identity || string.IsNullOrEmpty(identity))
            return value;

        if (identity.Length <= VisibleChars * 2)
            return Mask;

        return identity[..VisibleChars] + Mask + identity[^VisibleChars..];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
