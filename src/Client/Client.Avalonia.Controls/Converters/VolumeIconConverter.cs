using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Client.Avalonia.Controls.Icons;

namespace Client.Avalonia.Controls.Converters;

/// <summary>Speaker state for player volume sliders, whose values run from 0 to 100.</summary>
public sealed class VolumeIconConverter : IValueConverter
{
    public static readonly VolumeIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IconResources.Get(value is double volume && volume > 0
            ? volume <= 50 ? "LowVolumeIcon" : "HighVolumeIcon"
            : "CrossedNoVolumeIcon");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
