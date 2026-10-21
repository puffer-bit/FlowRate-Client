using Client.Avalonia.Localization;
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Shared.Enums;

namespace Client.Services;

public class VideoSessionStateToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HybridSessionPeerState state)
            return Strings.Get("session.peer.undefined");

        return state switch
        {
            HybridSessionPeerState.WaitingForApprove => Strings.Get("session.peer.waiting"),
            HybridSessionPeerState.Connected => Strings.Get("session.peer.connected"),
            _ => Strings.Get("session.peer.undefined")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

