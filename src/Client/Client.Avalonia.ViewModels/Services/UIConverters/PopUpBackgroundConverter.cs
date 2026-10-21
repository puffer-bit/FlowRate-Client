using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Client.ViewModels.Notifications;

namespace Client.Services;

/// <summary>
/// The background brush of a notification inside a hybrid session, by its kind.
/// </summary>
public class PopUpBackgroundConverter : IValueConverter
{
    public static readonly PopUpBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            NotificationPopUpVM.PopUpType.Warning => "PopUpWarningBrush",
            NotificationPopUpVM.PopUpType.Success => "PopUpSuccessBrush",
            NotificationPopUpVM.PopUpType.Error => "PopUpErrorBrush",
            NotificationPopUpVM.PopUpType.Loading => "PopUpLoadingBrush",
            NotificationPopUpVM.PopUpType.Question => "PopUpQuestionBrush",
            _ => "PopUpInfoBrush"
        };

        var app = global::Avalonia.Application.Current;
        return app != null && app.TryGetResource(key, app.ActualThemeVariant, out var resource)
            ? resource as IBrush
            : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
