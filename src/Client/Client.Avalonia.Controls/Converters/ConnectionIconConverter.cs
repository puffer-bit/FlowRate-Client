using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Client.Avalonia.Controls.Icons;

namespace Client.Avalonia.Controls.Converters;
public class ConnectionIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isConnected = value is bool b && b;

        return IconResources.Get(isConnected ? "DoorWithMarkIcon" : "DoorWithGreenArrowIcon");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

