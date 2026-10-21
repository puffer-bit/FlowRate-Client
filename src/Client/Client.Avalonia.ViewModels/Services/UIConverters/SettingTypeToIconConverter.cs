using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Client.ViewModels.Settings;

namespace Client.Services;

public class SettingTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SettingTabType type)
            return null;

        // Keys of DrawingImage resources from Client.Avalonia.Assets/IconImages.axaml
        var key = type switch
        {
            SettingTabType.AppAppearance    => "PaintRollerIcon",
            SettingTabType.Accounting    => "ShieldedUser",
            SettingTabType.AppBehavior  => "LightningIcon",
            SettingTabType.Audio  => "VoiceIcon",
            SettingTabType.HotKeys  => "KeycapIcon",
            SettingTabType.DebugAndLogs  => "DebugIcon",
            SettingTabType.Experimental  => "RadiationIcon",
            SettingTabType.PlatformSpecific  => CurrentPlatform.IconKey,
            SettingTabType.About  => "IInCircleIcon",
            SettingTabType.Storage  => "StorageIcon",
            SettingTabType.Video  => "VideoIcon",
            _ => null
        };

        var app = global::Avalonia.Application.Current;
        if (key is null || app is null)
            return null;

        return app.TryGetResource(key, app.ActualThemeVariant, out var icon) ? icon : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
